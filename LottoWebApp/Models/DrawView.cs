namespace LottoWebApp.Models
{
    public class DrawView
    {
        public int Draw { get; set; }
        public DateTime Date { get; set; }
        public string Time { get; set; } = "";

        // ВЫПАВШИЕ ЧИСЛА
        public List<int> Numbers { get; set; } = new();
        public List<int> Numbers1 { get; set; } = new(); // B11, B12, B13
        public List<int> Numbers2 { get; set; } = new(); // B21, B22, B23

        // СТАТИСТИКИ ДЛЯ ОБЩЕЙ ТАБЛИЦЫ (если нужно)
        public int Sum { get; set; }
        public int SumUnpicked { get; set; }
        public double Average { get; set; }
        public double Median { get; set; }
        public string HasRepeats { get; set; } = "нет";
        public double Variance { get; set; }
        public double StdDeviation { get; set; }
        public int Max { get; set; }
        public int Min { get; set; }
        public int EvenCount { get; set; }
        public int OddCount { get; set; }
        public int PrimeCount { get; set; }
        public int CompositeCount { get; set; }
        public int LowCount { get; set; }
        public int HighCount { get; set; }
        public int TriangularCount { get; set; }
        public int SquareCount { get; set; }
        public int MirroredCount { get; set; }

        // --- НОВОЕ: СТАТИСТИКИ ДЛЯ B1 И B2 ---
        public int Sum1 { get; set; }
        public int SumUnpicked1 { get; set; }
        public double Average1 { get; set; }
        public double Median1 { get; set; }
        public string HasRepeats1 { get; set; } = "нет";
        public double Variance1 { get; set; }
        public double StdDeviation1 { get; set; }
        public int Max1 { get; set; }
        public int Min1 { get; set; }
        public int EvenCount1 { get; set; }
        public int OddCount1 { get; set; }
        public int PrimeCount1 { get; set; }
        public int CompositeCount1 { get; set; }
        public int LowCount1 { get; set; }
        public int HighCount1 { get; set; }
        public int TriangularCount1 { get; set; }
        public int SquareCount1 { get; set; }
        public int MirroredCount1 { get; set; }

        public int Sum2 { get; set; }
        public int SumUnpicked2 { get; set; }
        public double Average2 { get; set; }
        public double Median2 { get; set; }
        public string HasRepeats2 { get; set; } = "нет";
        public double Variance2 { get; set; }
        public double StdDeviation2 { get; set; }
        public int Max2 { get; set; }
        public int Min2 { get; set; }
        public int EvenCount2 { get; set; }
        public int OddCount2 { get; set; }
        public int PrimeCount2 { get; set; }
        public int CompositeCount2 { get; set; }
        public int LowCount2 { get; set; }
        public int HighCount2 { get; set; }
        public int TriangularCount2 { get; set; }
        public int SquareCount2 { get; set; }
        public int MirroredCount2 { get; set; }
    }
}