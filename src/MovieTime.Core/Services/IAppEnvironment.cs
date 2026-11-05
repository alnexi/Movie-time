namespace MovieTime.Core.Services;

/// <summary>
/// Provides environment and runtime diagnostics about the current application.
/// </summary>
public interface IAppEnvironment
{
    string ApplicationName { get; }
    string ApplicationVersion { get; }
    string FrameworkDescription { get; }
    string OperatingSystem { get; }
    string ProcessArchitecture { get; }
    bool Is64BitProcess { get; }
}
