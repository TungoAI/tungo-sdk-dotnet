namespace Tungo.Sdk.Tests;

public static class ConformanceTests
{
    public static void Verify()
    {
        if (TungoMoney.ToTiyn(45000.0) != 4_500_000L) throw new System.Exception("ToTiyn failed");
        if (TungoMoney.FormatTiyn(4_500_000L, "KZT") != "45 000,00 ₸") throw new System.Exception("FormatTiyn failed");
    }
}
