namespace Kroira.UnitTests.Fixtures;

internal static class SyntheticSourceFixtures
{
    public const string M3uEscapedQuoteMissingCommaLine =
        "#EXTINF:-1 TVG-ID=fixture.one group-title='*** News |' tvg-logo=\"https://img.example/logo.png?size=small&token=safe\" tvg-name=\"Fixture \\\"Quoted\\\" Channel\" News Fixture";

    public const string M3uHeaderWithBomCommentsAndRelativeGuide =
        "\uFEFF#EXTM3U XMLTV=https://guide.example/a.xml?token=safe&x=1 url-tvg=relative-guide.xml\n" +
        "# Provider comment\n" +
        "#EXTVLCOPT:http-user-agent=Fixture\n" +
        "#EXTINF:-1 tvg-id=fixture.one,News Fixture\n" +
        "https://stream.example/live/fixture.m3u8?token=safe&profile=1\n";
}
