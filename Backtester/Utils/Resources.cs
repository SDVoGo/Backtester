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
            => ReadText($"{fileName}.pine");

        /// <summary>
        /// Return the content of an embedded file in Resources
        /// </summary>
        /// <param name="fileName">With extension</param>
        public static string ReadText(string fileName)
        {
            var assembly = typeof(Program).Assembly;

            using var stream = assembly.GetManifestResourceStream($"Backtester.Resources.{fileName}")
                ?? throw new FileNotFoundException($"Embedded resource '{fileName}' not found");

            using var reader = new StreamReader(stream);

            return reader.ReadToEnd();
        }
    }
}