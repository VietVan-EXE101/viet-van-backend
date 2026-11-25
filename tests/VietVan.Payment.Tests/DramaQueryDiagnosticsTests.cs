using Microsoft.EntityFrameworkCore;
using VietVan.API.Data;
using Xunit;
using Xunit.Abstractions;

public class DramaQueryDiagnosticsTests(ITestOutputHelper output)
{
    [Fact]
    public void PrintDetailQueryAndSceneMappingWithoutConnecting()
    {
        var options = new DbContextOptionsBuilder<VietVanDbContext>()
            .UseSqlServer("Server=localhost;Database=diagnostic;Integrated Security=true;TrustServerCertificate=true").Options;
        using var db = new VietVanDbContext(options);
        var query = db.DramaProductions.AsNoTracking().AsSplitQuery()
            .Include(x => x.Assets)
            .Include(x => x.Scenes).ThenInclude(x => x.BackgroundAsset)
            .Include(x => x.Scenes).ThenInclude(x => x.AudioAsset)
            .Include(x => x.Scenes).ThenInclude(x => x.VideoAsset)
            .Include(x => x.Scenes).ThenInclude(x => x.Hotspots).ThenInclude(x => x.VisualAsset)
            .Where(x => x.Id == 1);
        output.WriteLine("Detail split query (first command):\n" + query.ToQueryString());
        output.WriteLine("Scene mapping (separate diagnostic query, not the actual split child command):\n" + db.DramaScenes.AsNoTracking().Where(s => s.DramaProductionId == 1).ToQueryString());
        Assert.Contains("[DramaScenes]", db.DramaScenes.ToQueryString());
    }
}
