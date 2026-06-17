using WorldMonitor.Providers;
using Xunit;

namespace WorldMonitor.Api.Tests;

public class NasaFirmsProviderTests
{
    [Fact]
    public void MapCsv_reads_columns_by_header_name_and_skips_unparseable_rows()
    {
        // Header + 2 rows; the second row has an unparseable latitude and must be skipped.
        const string csv = """
        latitude,longitude,bright_ti4,scan,track,acq_date,acq_time,satellite,instrument,confidence,version,bright_ti5,frp,daynight
        -11.23,16.45,330.1,0.5,0.45,2026-06-16,1342,N,VIIRS,n,2.0NRT,295.3,12.7,N
        NaN-bad,16.45,330.1,0.5,0.45,2026-06-16,1342,N,VIIRS,n,2.0NRT,295.3,12.7,N
        """;

        var fires = NasaFirmsProvider.MapCsv(csv, 100);

        var f = Assert.Single(fires);
        Assert.Equal(-11.23, f.Latitude);
        Assert.Equal(16.45, f.Longitude);
        Assert.Equal(330.1, f.Brightness);
        Assert.Equal("n", f.Confidence);
        Assert.Equal("2026-06-16", f.AcqDate);
        Assert.Equal(12.7, f.Frp);
    }

    [Fact]
    public void MapCsv_reads_by_name_regardless_of_column_order()
    {
        // Reordered columns: header positions differ from the documented default.
        const string csv = """
        acq_date,frp,confidence,longitude,latitude,bright_ti4
        2026-06-16,12.7,h,16.45,-11.23,330.1
        """;

        var f = Assert.Single(NasaFirmsProvider.MapCsv(csv, 100));
        Assert.Equal(-11.23, f.Latitude);
        Assert.Equal(16.45, f.Longitude);
        Assert.Equal(330.1, f.Brightness);
        Assert.Equal("h", f.Confidence);
        Assert.Equal(12.7, f.Frp);
        Assert.Equal("2026-06-16", f.AcqDate);
    }

    [Fact]
    public void MapCsv_respects_the_count_limit()
    {
        const string csv = """
        latitude,longitude,bright_ti4,confidence,acq_date,frp
        1.0,2.0,300,n,2026-06-16,5.0
        3.0,4.0,310,h,2026-06-16,6.0
        5.0,6.0,320,l,2026-06-16,7.0
        """;

        Assert.Equal(2, NasaFirmsProvider.MapCsv(csv, 2).Count);
    }

    [Fact]
    public void MapCsv_returns_empty_for_blank_or_header_only_input()
    {
        const string headerOnly = "latitude,longitude,bright_ti4,confidence,acq_date,frp";
        Assert.Empty(NasaFirmsProvider.MapCsv(headerOnly, 100));
        Assert.Empty(NasaFirmsProvider.MapCsv("", 100));
        Assert.Empty(NasaFirmsProvider.MapCsv("   ", 100));
    }
}
