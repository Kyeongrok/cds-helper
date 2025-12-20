namespace CdsHelper.Support.Local.Models;

public class HintData
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public byte Value { get; set; }
    public bool IsAcquired => Value == 0x0D || Value == 0x01;

    public string DisplayText => $"{Index}: {Name}";
}
