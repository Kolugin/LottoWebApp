using LottoWebApp.Data;

namespace LottoWebApp.Services
{
    public class AppServices
    {
        public LottoDbContext Db { get; }
        public A11CsvExportHelper Csv11 { get; }
        public A12CsvExportHelper Csv12 { get; }
        public A13CsvExportHelper Csv13 { get; }
        public A14CsvExportHelper Csv14 { get; }
        public A15CsvExportHelper Csv15 { get; }
        public A16CsvExportHelper Csv16 { get; }
        public A17CsvExportHelper Csv17 { get; }
        public A18CsvExportHelper Csv18 { get; }
        public A19CsvExportHelper Csv19 { get; }
        public A110CsvExportHelper Csv110 { get; }
        public A111CsvExportHelper Csv111 { get; }
        public A112CsvExportHelper Csv112 { get; }
        public A113CsvExportHelper Csv113 { get; }
        public A114CsvExportHelper Csv114 { get; }
        public A115CsvExportHelper Csv115 { get; }
        public A116CsvExportHelper Csv116 { get; }
        public A117CsvExportHelper Csv117 { get; }
        public A118CsvExportHelper Csv118 { get; }

        public A21CsvExportHelper Csv21 { get; }
        public A22CsvExportHelper Csv22 { get; }
        public A23CsvExportHelper Csv23 { get; }

        public A31CsvExportHelper Csv31 { get; }
        public A32CsvExportHelper Csv32 { get; }
        public A33CsvExportHelper Csv33 { get; }
        public A34CsvExportHelper Csv34 { get; }
        public A35CsvExportHelper Csv35 { get; }
        public A36CsvExportHelper Csv36 { get; }
        public A37CsvExportHelper Csv37 { get; }
        public A38CsvExportHelper Csv38 { get; }
        public A39CsvExportHelper Csv39 { get; }

        public A310CsvExportHelper Csv310 { get; }
        public A311CsvExportHelper Csv311 { get; }
        public A312CsvExportHelper Csv312 { get; }
        public A313CsvExportHelper Csv313 { get; }
        public A314CsvExportHelper Csv314 { get; }
        public A315CsvExportHelper Csv315 { get; }
        public A316CsvExportHelper Csv316 { get; }
        public A317CsvExportHelper Csv317 { get; }
        public A318CsvExportHelper Csv318 { get; }
        public A319CsvExportHelper Csv319 { get; }
        public A320CsvExportHelper Csv320 { get; }
        public A321CsvExportHelper Csv321 { get; }

        public A41CsvExportHelper Csv41 { get; }
        public A42CsvExportHelper Csv42 { get; }

        public A51CsvExportHelper Csv51 { get; }
        public A52CsvExportHelper Csv52 { get; }
        public A53CsvExportHelper Csv53 { get; }
        public A54CsvExportHelper Csv54 { get; }

        public A61CsvExportHelper Csv61 { get; }
        public A62CsvExportHelper Csv62 { get; }
        public A63CsvExportHelper Csv63 { get; }
        public A64CsvExportHelper Csv64 { get; }
        public A65CsvExportHelper Csv65 { get; }
        public ICacheService Cache { get; }

        public AppServices(
            LottoDbContext db,

            A11CsvExportHelper csv11,
            A12CsvExportHelper csv12,
            A13CsvExportHelper csv13,
            A14CsvExportHelper csv14,
            A15CsvExportHelper csv15,
            A16CsvExportHelper csv16,
            A17CsvExportHelper csv17,
            A18CsvExportHelper csv18,
            A19CsvExportHelper csv19,
            A110CsvExportHelper csv110,
            A111CsvExportHelper csv111,
            A112CsvExportHelper csv112,
            A113CsvExportHelper csv113,
            A114CsvExportHelper csv114,
            A115CsvExportHelper csv115,
            A116CsvExportHelper csv116,
            A117CsvExportHelper csv117,
            A118CsvExportHelper csv118,

            A21CsvExportHelper csv21,
            A22CsvExportHelper csv22,
            A23CsvExportHelper csv23,

            A31CsvExportHelper csv31,
            A32CsvExportHelper csv32,
            A33CsvExportHelper csv33,
            A34CsvExportHelper csv34,
            A35CsvExportHelper csv35,
            A36CsvExportHelper csv36,
            A37CsvExportHelper csv37,
            A38CsvExportHelper csv38,
            A39CsvExportHelper csv39,
            A310CsvExportHelper csv310,
            A311CsvExportHelper csv311,
            A312CsvExportHelper csv312,
            A313CsvExportHelper csv313,
            A314CsvExportHelper csv314,
            A315CsvExportHelper csv315,
            A316CsvExportHelper csv316,
            A317CsvExportHelper csv317,
            A318CsvExportHelper csv318,
            A319CsvExportHelper csv319,
            A320CsvExportHelper csv320,
            A321CsvExportHelper csv321,

            A41CsvExportHelper csv41,
            A42CsvExportHelper csv42,

            A51CsvExportHelper csv51,
            A52CsvExportHelper csv52,
            A53CsvExportHelper csv53,
            A54CsvExportHelper csv54,

            A61CsvExportHelper csv61,
            A62CsvExportHelper csv62,
            A63CsvExportHelper csv63,
            A64CsvExportHelper csv64,
            A65CsvExportHelper csv65,

            ICacheService cache)
        {
            Db = db;
            Csv11 = csv11;
            Csv12 = csv12;
            Csv13 = csv13;
            Csv14 = csv14;
            Csv15 = csv15;
            Csv16 = csv16;
            Csv17 = csv17;
            Csv18 = csv18;
            Csv19 = csv19;
            Csv110 = csv110;
            Csv111 = csv111;
            Csv112 = csv112;
            Csv113 = csv113;
            Csv114 = csv114;
            Csv115 = csv115;
            Csv116 = csv116;
            Csv117 = csv117;
            Csv118 = csv118;

            Csv21 = csv21;
            Csv22 = csv22;
            Csv23 = csv23;

            Csv31 = csv31;
            Csv32 = csv32;
            Csv33 = csv33;
            Csv34 = csv34;
            Csv35 = csv35;
            Csv36 = csv36;
            Csv37 = csv37;
            Csv38 = csv38;
            Csv39 = csv39;
            Csv310 = csv310;
            Csv311 = csv311;
            Csv312 = csv312;
            Csv313 = csv313;
            Csv314 = csv314;
            Csv315 = csv315;
            Csv316 = csv316;
            Csv317 = csv317;
            Csv318 = csv318;
            Csv319 = csv319;
            Csv320 = csv320;
            Csv321 = csv321;

            Csv41 = csv41;
            Csv42 = csv42;

            Csv51 = csv51;
            Csv52 = csv52;
            Csv53 = csv53;
            Csv54 = csv54;

            Csv61 = csv61;
            Csv62 = csv62;
            Csv63 = csv63;
            Csv64 = csv64;
            Csv65 = csv65;

            Cache = cache;
        }
    }
}
