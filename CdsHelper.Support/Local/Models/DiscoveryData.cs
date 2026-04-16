namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 세이브 파일의 발견물 슬롯 (BASE = 0x19E6A, ROW = 164바이트, 발견물 ID로 인덱싱)
/// 슬롯의 첫 바이트(state):
///   bit 6 (0x40) = 발견됨
///   bit 7 (0x80) = 발표됨
///   하위 6비트 = 발견물 종류별 base 값 (0x0C: 건축물, 0x04: 일부 교회, 0x06: 동물 등)
/// </summary>
public class DiscoveryData
{
    public int Id { get; set; }
    public byte State { get; set; }

    public bool IsDiscovered => (State & 0x40) != 0;
    public bool IsAnnounced => (State & 0x80) != 0;
}
