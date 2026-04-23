namespace TraceHunter.Web;

public static class Program
{
    public static void Main(string[] args)
    {
        // Phase 0 scaffolding stub. The real Blazor Server host lands in a later phase.
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();
        app.Run();
    }
}
