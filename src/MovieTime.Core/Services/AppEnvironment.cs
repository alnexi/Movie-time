using System.Runtime.InteropServices;

namespace MovieTime.Core.Services;

/// <summary>
/// Default implementation of <see cref="IAppEnvironment"/> reading host system information.
/// </summary>
public sealed class AppEnvironment : IAppEnvironment
{
    public string ApplicationName => AppConstants.ApplicationName;
    public string ApplicationVersion => AppConstants.ApplicationVersion;
    public string FrameworkDescription => RuntimeInformation.FrameworkDescription;
    public string OperatingSystem => RuntimeInformation.OSDescription;
    public string ProcessArchitecture => RuntimeInformation.ProcessArchitecture.ToString();
    public bool Is64BitProcess => Environment.Is64BitProcess;
}
