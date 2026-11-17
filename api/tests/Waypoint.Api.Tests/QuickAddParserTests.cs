using Waypoint.Api.Domain;

namespace Waypoint.Api.Tests;

public class QuickAddParserTests
{
    [Fact]
    public void Parses_title_project_and_priority()
    {
        var r = QuickAddParser.Parse("Review AG failover #origination !1");
        Assert.Equal("Review AG failover", r.Title);
        Assert.Equal("origination", r.ProjectToken);
        Assert.Equal(1, r.Priority);
    }

    [Fact]
    public void Tokens_can_appear_anywhere()
    {
        var r = QuickAddParser.Parse("!3 #admin  expense   report");
        Assert.Equal("expense report", r.Title);
        Assert.Equal("admin", r.ProjectToken);
        Assert.Equal(3, r.Priority);
    }

    [Fact]
    public void Plain_text_has_no_project_or_priority()
    {
        var r = QuickAddParser.Parse("Call the auditor");
        Assert.Equal("Call the auditor", r.Title);
        Assert.Null(r.ProjectToken);
        Assert.Null(r.Priority);
    }

    [Theory]
    [InlineData("Fix bug !4", "Fix bug !4")]
    [InlineData("Wow! great", "Wow! great")]
    [InlineData("Ticket # 42", "Ticket # 42")]
    public void Lookalike_tokens_stay_in_the_title(string input, string title)
    {
        var r = QuickAddParser.Parse(input);
        Assert.Equal(title, r.Title);
        Assert.Null(r.Priority);
        Assert.Null(r.ProjectToken);
    }

    [Fact]
    public void Only_the_first_hash_token_is_the_project()
    {
        var r = QuickAddParser.Parse("Map #sox controls to #origination");
        Assert.Equal("sox", r.ProjectToken);
        Assert.Equal("Map controls to #origination", r.Title);
    }

    [Theory]
    [InlineData("origination", "Origination")]
    [InlineData("ORIGINATION", "Origination")]
    [InlineData("sox", "SOX/J-SOX")]
    [InlineData("sox/j-sox", "SOX/J-SOX")]
    [InlineData("jsox", "SOX/J-SOX")]
    [InlineData("extractor", "Extractor migration")]
    [InlineData("extractor-migration", "Extractor migration")]
    [InlineData("people", "People management")]
    [InlineData("admin", "Admin")]
    public void Matches_projects_by_slug_prefix_or_substring(string token, string expected)
    {
        var projects = new[] { "SOX/J-SOX", "Origination", "Extractor migration", "People management", "Admin" };
        Assert.Equal(expected, QuickAddParser.MatchProject(token, projects, p => p));
    }

    [Fact]
    public void Ambiguous_or_unknown_tokens_do_not_match()
    {
        var projects = new[] { "Origination", "Orientation" };
        Assert.Null(QuickAddParser.MatchProject("ori", projects, p => p));
        Assert.Null(QuickAddParser.MatchProject("payroll", projects, p => p));
        Assert.Equal("Origination", QuickAddParser.MatchProject("orig", projects, p => p));
    }
}
