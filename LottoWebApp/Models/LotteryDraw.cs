namespace LottoWebApp.Models
{
    public abstract class LotteryDraw
    {
        public int Draw { get; set; }
        public string Date { get; set; }
        public List<int> Numbers { get; set; } = new List<int>();
    }
}
