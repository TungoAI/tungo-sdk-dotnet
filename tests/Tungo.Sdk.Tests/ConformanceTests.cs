namespace Tungo.Sdk.Tests;

public static class ConformanceTests
{
    public static void Verify()
    {
        if (TungoMoney.ToTiyn(45000.0) != 4_500_000L) throw new System.Exception("ToTiyn failed");
        if (TungoMoney.FormatTiyn(4_500_000L, "KZT") != "45 000,00 ₸") throw new System.Exception("FormatTiyn failed");
        if (!TungoMoney.IsValidBinIin("990140000017")) throw new System.Exception("IsValidBinIin valid failed");
        if (!TungoMoney.IsValidBinIin("991140000015")) throw new System.Exception("IsValidBinIin pass2 failed");
        if (TungoMoney.IsValidBinIin("990140000016")) throw new System.Exception("IsValidBinIin invalid failed");
    }
}
