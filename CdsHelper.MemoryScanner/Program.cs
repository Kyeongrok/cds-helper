using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CdsHelper.MemoryScanner;

/// <summary>
/// CDS95.exe 프로세스 메모리에서 좌표 주소 찾기 도구.
///
/// 사용법:
///   1. 대항해시대3 게임 실행
///   2. dotnet run --project CdsHelper.MemoryScanner
///   3. 메뉴에 따라 스냅샷 찍고 비교
///
/// 동작:
///   - CDS95 프로세스의 writable 메모리 페이지 전부를 스냅샷
///   - 스냅샷 두 장 비교 → 델타(변화량)가 특정 패턴인 주소 추출
///   - 주요 포맷(2바이트/4바이트/float, 부호 있음/없음) 모두 자동 체크
/// </summary>
internal static class Program
{
    private const string ProcessName = "cds_95";

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(
        IntPtr hProcess, IntPtr address, byte[] buffer, int size, out int read);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(
        IntPtr hProcess, IntPtr address, byte[] buffer, int size, out int written);

    [DllImport("kernel32.dll")]
    private static extern int VirtualQueryEx(
        IntPtr hProcess, IntPtr address, out MEMORY_BASIC_INFORMATION buffer, int length);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_BASIC_INFORMATION
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public IntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint MEM_COMMIT = 0x1000;
    private const uint PAGE_READWRITE = 0x04;
    private const uint PAGE_WRITECOPY = 0x08;
    private const uint PAGE_EXECUTE_READWRITE = 0x40;
    private const uint PAGE_EXECUTE_WRITECOPY = 0x80;

    /// <summary>스냅샷 1장: 주소 → 바이트 배열.</summary>
    private sealed class Snapshot
    {
        public Dictionary<IntPtr, byte[]> Regions { get; } = new();
        public long TotalBytes { get; set; }
    }

    private static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=== CDS95 Memory Scanner ===\n");

        var process = Process.GetProcessesByName(ProcessName).FirstOrDefault();
        if (process == null)
        {
            Console.WriteLine($"'{ProcessName}.exe' 프로세스를 찾을 수 없음. 게임 먼저 실행하세요.");
            return;
        }

        Console.WriteLine($"프로세스 발견: PID={process.Id}, Base=0x{process.MainModule?.BaseAddress.ToInt64():X}");

        var handle = OpenProcess(PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION | PROCESS_QUERY_INFORMATION, false, process.Id);
        if (handle == IntPtr.Zero)
        {
            Console.WriteLine($"OpenProcess 실패. 관리자 권한으로 실행했나요? (에러 {Marshal.GetLastWin32Error()})");
            return;
        }

        try
        {
            Run(handle);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static void Run(IntPtr handle)
    {
        Snapshot? snapA = null;
        Snapshot? snapB = null;
        List<IntPtr>? candidates = null;

        while (true)
        {
            Console.WriteLine("\n--- 메뉴 ---");
            Console.WriteLine("  A                  : 스냅샷 A 찍기");
            Console.WriteLine("  B                  : 스냅샷 B 찍기");
            Console.WriteLine("  D <도>             : A/B 비교");
            Console.WriteLine("  R <도>             : 기존 후보 대상으로 재비교");
            Console.WriteLine("  V                  : 후보 값 보기");
            Console.WriteLine("  W <주소hex> <값>   : 메모리 쓰기 (2바이트) — 예: 'W 2FA0238 2700'");
            Console.WriteLine("  S <주소hex>        : 주소 1개 값 읽기");
            Console.WriteLine("  F <값> [tol]       : 정확한 값 찾기 (16/32비트). tol=오차범위(기본 0)");
            Console.WriteLine("  N <값> [tol]       : 기존 후보 중에서 값 매치 (좁히기)");
            Console.WriteLine("  Q                  : 종료");
            Console.Write("> ");
            var line = Console.ReadLine()?.Trim() ?? "";
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var key = parts.Length > 0 ? parts[0].ToUpperInvariant() : "";
            int degrees = 1;
            if (parts.Length > 1) int.TryParse(parts[1], out degrees);

            switch (key)
            {
                case "A":
                    snapA = TakeSnapshot(handle);
                    Console.WriteLine($"스냅샷 A 저장됨. 영역 {snapA.Regions.Count}개, {snapA.TotalBytes / 1024 / 1024} MB");
                    break;

                case "B":
                    snapB = TakeSnapshot(handle);
                    Console.WriteLine($"스냅샷 B 저장됨. 영역 {snapB.Regions.Count}개, {snapB.TotalBytes / 1024 / 1024} MB");
                    break;

                case "D":
                    if (snapA == null || snapB == null) { Console.WriteLine("A, B 둘 다 찍어야 함"); break; }
                    Console.WriteLine($"이동 도수 = {degrees}");
                    candidates = DiffSnapshots(snapA, snapB, degrees);
                    Console.WriteLine($"\n후보 주소 {candidates.Count}개");
                    if (candidates.Count > 0 && candidates.Count <= 50)
                        PrintCandidates(handle, candidates);
                    break;

                case "R":
                    if (candidates == null || candidates.Count == 0) { Console.WriteLine("먼저 D로 후보를 만들어야 함"); break; }
                    if (snapA == null || snapB == null) { Console.WriteLine("A, B 둘 다 다시 찍어야 함"); break; }
                    Console.WriteLine($"이동 도수 = {degrees}");
                    candidates = DiffSnapshots(snapA, snapB, degrees, candidates);
                    Console.WriteLine($"\n후보 {candidates.Count}개로 좁혀짐");
                    if (candidates.Count > 0 && candidates.Count <= 50)
                        PrintCandidates(handle, candidates);
                    break;

                case "V":
                    if (candidates == null) { Console.WriteLine("후보 없음"); break; }
                    PrintCandidates(handle, candidates);
                    break;

                case "W":
                    if (parts.Length < 3)
                    {
                        Console.WriteLine("사용법: W <주소hex> <값>   예: W 2FA0238 2700");
                        break;
                    }
                    if (!long.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out var writeAddr))
                    {
                        Console.WriteLine("주소 hex 파싱 실패");
                        break;
                    }
                    if (!short.TryParse(parts[2], out var writeValue))
                    {
                        Console.WriteLine("값 파싱 실패 (2바이트 범위)");
                        break;
                    }
                    var writeBuf = BitConverter.GetBytes(writeValue);
                    if (WriteProcessMemory(handle, new IntPtr(writeAddr), writeBuf, 2, out int written))
                        Console.WriteLine($"0x{writeAddr:X} <- {writeValue} 쓰기 성공 ({written}바이트)");
                    else
                        Console.WriteLine($"쓰기 실패: Win32Error={Marshal.GetLastWin32Error()}");
                    break;

                case "F":
                {
                    if (parts.Length < 2)
                    {
                        Console.WriteLine("사용법: F <값> [tol]   예: F 1125  또는  F 1125 5");
                        break;
                    }
                    if (!int.TryParse(parts[1], out int findValue))
                    {
                        Console.WriteLine("값 파싱 실패");
                        break;
                    }
                    int tolerance = 0;
                    if (parts.Length > 2) int.TryParse(parts[2], out tolerance);

                    // 라이브 메모리 다시 스캔
                    var liveSnap = TakeSnapshot(handle);
                    var matches = FindByValue(liveSnap, findValue, tolerance);
                    candidates = matches;
                    Console.WriteLine($"\n값 {findValue} (±{tolerance}) 매치: {matches.Count}개");
                    if (matches.Count > 0 && matches.Count <= 50)
                        PrintCandidates(handle, matches);
                    break;
                }

                case "N":
                {
                    if (candidates == null || candidates.Count == 0) { Console.WriteLine("기존 후보 없음"); break; }
                    if (parts.Length < 2)
                    {
                        Console.WriteLine("사용법: N <값> [tol]");
                        break;
                    }
                    if (!int.TryParse(parts[1], out int findValue))
                    {
                        Console.WriteLine("값 파싱 실패");
                        break;
                    }
                    int tolerance = 0;
                    if (parts.Length > 2) int.TryParse(parts[2], out tolerance);

                    var newCandidates = new List<IntPtr>();
                    foreach (var addr in candidates)
                    {
                        var buf = new byte[4];
                        if (!ReadProcessMemory(handle, addr, buf, 4, out _)) continue;
                        short i16 = BitConverter.ToInt16(buf, 0);
                        int i32 = BitConverter.ToInt32(buf, 0);
                        if (Math.Abs(i16 - findValue) <= tolerance || Math.Abs(i32 - findValue) <= tolerance)
                            newCandidates.Add(addr);
                    }
                    candidates = newCandidates;
                    Console.WriteLine($"\n값 {findValue} (±{tolerance})에 매치되는 기존 후보: {newCandidates.Count}개");
                    if (newCandidates.Count > 0 && newCandidates.Count <= 50)
                        PrintCandidates(handle, newCandidates);
                    break;
                }

                case "S":
                    if (parts.Length < 2)
                    {
                        Console.WriteLine("사용법: S <주소hex>");
                        break;
                    }
                    if (!long.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out var readAddr))
                    {
                        Console.WriteLine("주소 hex 파싱 실패");
                        break;
                    }
                    var readBuf = new byte[4];
                    if (ReadProcessMemory(handle, new IntPtr(readAddr), readBuf, 4, out _))
                    {
                        Console.WriteLine($"0x{readAddr:X}: i16={BitConverter.ToInt16(readBuf, 0),6}  i32={BitConverter.ToInt32(readBuf, 0),12}  float={BitConverter.ToSingle(readBuf, 0):G6}");
                    }
                    else
                    {
                        Console.WriteLine($"읽기 실패: Win32Error={Marshal.GetLastWin32Error()}");
                    }
                    break;

                case "Q":
                    return;
            }
        }
    }

    private static Snapshot TakeSnapshot(IntPtr handle)
    {
        var snap = new Snapshot();
        IntPtr addr = IntPtr.Zero;
        int mbiSize = Marshal.SizeOf<MEMORY_BASIC_INFORMATION>();

        while (VirtualQueryEx(handle, addr, out var mbi, mbiSize) != 0)
        {
            long regionSize = mbi.RegionSize.ToInt64();
            if (regionSize <= 0) break;

            bool isCommitted = mbi.State == MEM_COMMIT;
            bool isWritable = (mbi.Protect & (PAGE_READWRITE | PAGE_WRITECOPY | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)) != 0;

            if (isCommitted && isWritable && regionSize < 128 * 1024 * 1024)
            {
                var buf = new byte[regionSize];
                if (ReadProcessMemory(handle, mbi.BaseAddress, buf, buf.Length, out int read) && read > 0)
                {
                    if (read != buf.Length)
                    {
                        var trimmed = new byte[read];
                        Array.Copy(buf, trimmed, read);
                        snap.Regions[mbi.BaseAddress] = trimmed;
                        snap.TotalBytes += read;
                    }
                    else
                    {
                        snap.Regions[mbi.BaseAddress] = buf;
                        snap.TotalBytes += read;
                    }
                }
            }

            long next = mbi.BaseAddress.ToInt64() + regionSize;
            if (next <= addr.ToInt64()) break;
            addr = new IntPtr(next);
        }

        return snap;
    }

    /// <summary>
    /// 두 스냅샷을 비교해 '의미 있는' 델타를 가진 주소를 반환.
    /// degrees = 화면에서 몇 도 이동했는지. 툴이 이걸 기반으로 가능한 스케일(1x, 10x, 60x, 100x) 델타를 생성.
    /// </summary>
    private static List<IntPtr> DiffSnapshots(Snapshot a, Snapshot b, int degrees, List<IntPtr>? filter = null)
    {
        var result = new List<IntPtr>();
        var filterSet = filter == null ? null : new HashSet<IntPtr>(filter);

        // degrees 기반 델타: 1x(도), 10x, 60x(분), 100x, 2x, 3600x(초) 등 + 부호
        int[] scales = { 1, 2, 10, 60, 100, 3600 };
        var intDeltaList = new List<int>();
        foreach (var s in scales)
        {
            intDeltaList.Add(degrees * s);
            intDeltaList.Add(-degrees * s);
        }
        int[] intDeltas = intDeltaList.Distinct().ToArray();
        float[] floatDeltas = { degrees, -degrees, degrees * 0.1f, -degrees * 0.1f };

        foreach (var (baseAddr, bufA) in a.Regions)
        {
            if (!b.Regions.TryGetValue(baseAddr, out var bufB)) continue;
            int len = Math.Min(bufA.Length, bufB.Length);

            for (int i = 0; i <= len - 2; i++)
            {
                var addr = new IntPtr(baseAddr.ToInt64() + i);
                if (filterSet != null && !filterSet.Contains(addr)) continue;

                // int16 signed
                short sa = BitConverter.ToInt16(bufA, i);
                short sb = BitConverter.ToInt16(bufB, i);
                if (sa != sb)
                {
                    int delta = sb - sa;
                    if (Array.IndexOf(intDeltas, delta) >= 0)
                    {
                        result.Add(addr);
                        continue;
                    }
                }

                // int32
                if (i <= len - 4)
                {
                    int ia = BitConverter.ToInt32(bufA, i);
                    int ib = BitConverter.ToInt32(bufB, i);
                    if (ia != ib)
                    {
                        long delta = (long)ib - ia;
                        if (Array.IndexOf(intDeltas, (int)delta) >= 0)
                        {
                            result.Add(addr);
                            continue;
                        }
                    }

                    // float
                    float fa = BitConverter.ToSingle(bufA, i);
                    float fb = BitConverter.ToSingle(bufB, i);
                    if (!float.IsNaN(fa) && !float.IsNaN(fb) && fa != fb)
                    {
                        float d = fb - fa;
                        foreach (var fd in floatDeltas)
                        {
                            if (Math.Abs(d - fd) < 0.001f)
                            {
                                result.Add(addr);
                                break;
                            }
                        }
                    }
                }
            }
        }

        return result.Distinct().ToList();
    }

    /// <summary>
    /// 라이브 스냅샷에서 특정 값(±tolerance)을 가진 모든 주소를 찾음.
    /// 16비트, 32비트 둘 다 검사. 노이즈 영역(DLL 등 0x70000000+)은 제외.
    /// </summary>
    private static List<IntPtr> FindByValue(Snapshot snap, int target, int tolerance)
    {
        var result = new List<IntPtr>();
        foreach (var (baseAddr, buf) in snap.Regions)
        {
            // DLL 영역 제외 (kernel32 등) — 보통 game heap은 0x10000000 이하
            if (baseAddr.ToInt64() >= 0x10000000L) continue;

            for (int i = 0; i <= buf.Length - 2; i++)
            {
                short i16 = BitConverter.ToInt16(buf, i);
                if (Math.Abs(i16 - target) <= tolerance)
                {
                    result.Add(new IntPtr(baseAddr.ToInt64() + i));
                    continue;
                }
                if (i <= buf.Length - 4)
                {
                    int i32 = BitConverter.ToInt32(buf, i);
                    if (Math.Abs(i32 - target) <= tolerance)
                        result.Add(new IntPtr(baseAddr.ToInt64() + i));
                }
            }
        }
        return result.Distinct().ToList();
    }

    private static void PrintCandidates(IntPtr handle, List<IntPtr> candidates)
    {
        Console.WriteLine($"\n{"Address",-18} {"i16",6} {"i32",12} {"float",14}");
        Console.WriteLine(new string('-', 55));
        int shown = 0;
        foreach (var addr in candidates.Take(50))
        {
            var buf = new byte[4];
            if (!ReadProcessMemory(handle, addr, buf, 4, out _)) continue;
            short i16 = BitConverter.ToInt16(buf, 0);
            int i32 = BitConverter.ToInt32(buf, 0);
            float f = BitConverter.ToSingle(buf, 0);
            Console.WriteLine($"0x{addr.ToInt64():X12}  {i16,6} {i32,12} {f,14:G6}");
            shown++;
        }
        if (candidates.Count > shown)
            Console.WriteLine($"... ({candidates.Count - shown}개 생략)");
    }
}
