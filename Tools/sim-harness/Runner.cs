public static class Runner
{
    public static int Main(string[] args) => new NUnitLite.AutoRun(typeof(Runner).Assembly).Execute(args);
}
