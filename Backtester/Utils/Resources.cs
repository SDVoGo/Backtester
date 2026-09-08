namespace Utils
{
    public static class Resources
    {
        /// <summary>
        /// Return the file content
        /// </summary>
        /// <param name="fileName">Without extension</param>
        /// <returns></returns>
        public static string ReadFile(string fileName)
        {
            var assembly = typeof(Program).Assembly;

            using var stream = assembly.GetManifestResourceStream($"Backtester.Resources.{fileName}.pine");

            using var reader = new StreamReader(stream!);

            var grammar = reader.ReadToEnd();
            return grammar;
        }
    }
}