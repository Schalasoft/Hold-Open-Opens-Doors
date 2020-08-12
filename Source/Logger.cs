namespace HOOD
{
    public static class L
    {
        public static bool loggingEnabled = false;
        private const string logPrefix = "HOOD :: ";

        public static void Log(string log)
        {
            if (L.loggingEnabled) Verse.Log.Warning(logPrefix + log);
        }
    }
}
