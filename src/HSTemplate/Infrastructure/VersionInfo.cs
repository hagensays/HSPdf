using System.Reflection;

namespace HSTemplate.Infrastructure
{
    internal static class VersionInfo
    {
        public static string DisplayVersion
        {
            get
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                if (version == null)
                {
                    return "v0.0.0";
                }

                return string.Format("v{0}.{1}.{2}", version.Major, version.Minor, version.Build);
            }
        }
    }
}
