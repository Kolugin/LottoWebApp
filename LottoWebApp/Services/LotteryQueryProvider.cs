using System.Linq;
using LottoWebApp.Data;
using Microsoft.EntityFrameworkCore;

public class LotteryQueryProvider
{
    private readonly LottoDbContext _db;

    public LotteryQueryProvider(LottoDbContext db)
    {
        _db = db;
    }

    public IQueryable<object> GetQuery(string game)
    {
        if (string.IsNullOrWhiteSpace(game))
            game = "keno";

        switch (game.ToLower())
        {
            case "keno":
                return _db.LotteryKenoBY.AsNoTracking();

            case "blitz":
                return _db.LotteryBlitzBY.AsNoTracking();

            case "5x36":
                return _db.Lottery536BY.AsNoTracking();

            case "6x49":
                return _db.Lottery649BY.AsNoTracking();

            case "1224":
                return _db.Lottery1224BY.AsNoTracking();

            case "320":
                return _db.Lottery320BY.AsNoTracking();

            default:
                return _db.LotteryKenoBY.AsNoTracking();
        }
    }
}
