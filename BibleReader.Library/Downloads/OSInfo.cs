using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Runtime.Versioning;

#if NETSTANDARD

namespace System.Runtime.Versioning
{
    [AttributeUsage(
        AttributeTargets.Field |
        AttributeTargets.Method |
        AttributeTargets.Property,
        AllowMultiple = true,
        Inherited = false)]
    public sealed class SupportedOSPlatformGuardAttribute : Attribute
    {
        public SupportedOSPlatformGuardAttribute(string platformName)
        {
        }
    }
}
#endif

namespace BibleReader;


[Flags]
public enum OSPlatform { Unknown = 0, None = 0, Windows = 1, Unix = 2, Mac = 4, Linux = 8, Other = 0x10, All = 0x1F };
public enum OSFlavor { Unknown = 0, Min = 0, Windows, Mac, Debian, Mint, Kali, Ubuntu, Fedora, RedHat, Oracle, CentOS, Alma, Rocky, SUSE, Alpine, Arch, FreeBSD, NetBSD, Other, Max = Other }

public class OSInfo
{
	public static bool IsMono => Type.GetType("Mono.Runtime") != null;
	public static bool IsCore => !(IsNetFX || IsNetNative);
        [SupportedOSPlatformGuard("windows")]
        public static bool IsNetFX => RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.OrdinalIgnoreCase);
        [SupportedOSPlatformGuard("windows")]
        public static bool IsNetNative => RuntimeInformation.FrameworkDescription.StartsWith(".NET Native", StringComparison.OrdinalIgnoreCase);
        [SupportedOSPlatformGuard("windows")] 
	public static bool IsWindows => RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
        [SupportedOSPlatformGuard("linux")]
        public static bool IsLinux => RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
        [SupportedOSPlatformGuard("macos")]
        public static bool IsMac => RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX);
	public static bool IsArm => Architecture == Architecture.Arm64 || Architecture == Architecture.Arm;
	public static bool IsIntel => Architecture == Architecture.X64 || Architecture == Architecture.X86;
        [SupportedOSPlatformGuard("windows")]
        public static bool IsNet48 => !IsMono && IsWindows && Regex.IsMatch(RuntimeInformation.FrameworkDescription, @"^\.NET Framework 4\.[8-9]");
	public static bool Is64 => Environment.Is64BitOperatingSystem;
	public static bool Is32 => !Is64;
        [SupportedOSPlatformGuard("windows")]
        public static bool IsWindowsServer
	{
		get
		{
			if (!IsWindows) return false;
			var version = WindowsVersion;
			var vertxt = Enum.GetName(typeof(WindowsVersion), version);
			return vertxt.Contains("Server");
		}
	}
        [SupportedOSPlatformGuard("windows")]
        public static bool IsWindowsDesktop
	{
		get
		{
			if (!IsWindows) return false;
			var version = WindowsVersion;
			var vertxt = Enum.GetName(typeof(WindowsVersion), version);
			return !vertxt.Contains("Server");
		}
	}

	public static string FrameworkDescription => RuntimeInformation.FrameworkDescription;

	public static readonly System.Runtime.InteropServices.OSPlatform FreeBSD = System.Runtime.InteropServices.OSPlatform.Create("FREEBSD");
	public static readonly System.Runtime.InteropServices.OSPlatform NetBSD = System.Runtime.InteropServices.OSPlatform.Create("NETBSD");

        [SupportedOSPlatformGuard("linux")]
        [SupportedOSPlatformGuard("macos")]
        [SupportedOSPlatformGuard("freebsd")]
        [SupportedOSPlatformGuard("netbsd")]
        public static bool IsUnix => IsLinux || IsMac || IsFreeBSD || IsNetBSD;
	public static bool IsFreeBSD => RuntimeInformation.IsOSPlatform(FreeBSD);
	public static bool IsNetBSD => RuntimeInformation.IsOSPlatform(NetBSD);
        [SupportedOSPlatformGuard("linux")]
        public static bool IsSystemd => IsLinux && Directory.Exists("/run/systemd/system");
        [SupportedOSPlatformGuard("linux")]
        public static bool IsOpenRC => IsLinux && File.Exists("/etc/rc.conf") && Shell.Standard.Find("rc-status") != null;
        [SupportedOSPlatformGuard("linux")]
        public static bool IsWSL => IsLinux && File.Exists("/proc/version") && File.ReadAllText("/proc/version").ToLower().Contains("microsoft");
        [SupportedOSPlatformGuard("linux")]
        public static bool IsLinuxMusl
        {
            get
            {
                if (!IsLinux) return false;

                var info = new ProcessStartInfo("ldd");
                info.Arguments = "/bin/ls";
                info.RedirectStandardOutput = true;
                info.UseShellExecute = false;
                var p = Process.Start(info);
                return p.StandardOutput.ReadToEnd().Contains("musl");
            }
        }

        static OSFlavor flavor = OSFlavor.Unknown;
	static Version version = new Version("0.0.0.0");

	static Version ExtractVersion(string name) => new Version(Regex.Match(name, @"[0-9]+(?:\.[0-9]){0,3}").Value);
        public static Version NetVersion
	{
		get
		{
			if (IsMono || IsCore) return ExtractVersion(FrameworkDescription);

			return NetFXVersion;
		}
	}
	public static Version NetFXVersion => ExtractVersion(WindowsOSInfo.NetFXVersion); 

	public static string NetDescription
	{
		get
		{
			if (IsMono || IsCore) return FrameworkDescription;
			return $".NET Framework {NetVersion.ToString(3)}";
		}
	}
	public static OSPlatform OSPlatform => IsWindows ? OSPlatform.Windows :
		 (IsMac ? OSPlatform.Mac :
		 (IsLinux ? OSPlatform.Linux :
		 (IsNetBSD || IsFreeBSD ? OSPlatform.Unix : OSPlatform.Other)));

	public static Architecture Architecture => RuntimeInformation.ProcessArchitecture;
	public static OSFlavor OSFlavor
	{
		get
		{
			if (flavor != OSFlavor.Unknown) return flavor;
			version = Environment.OSVersion.Version;
			if (IsWindows) return flavor = OSFlavor.Windows;
			if (IsMac) return flavor = OSFlavor.Mac;
			if (IsFreeBSD) return flavor = OSFlavor.FreeBSD;
			if (IsNetBSD) return flavor = OSFlavor.NetBSD;
			if (IsLinux)
			{
				string name = null;
				const string OsReleaseFile = "/etc/os-release";
				if (File.Exists(OsReleaseFile))
				{
					var osRelease = File.ReadAllText(OsReleaseFile);
					var match = Regex.Match(osRelease, "(?<=^NAME\\s*=\\s*\")[^\"]+(?=\")", RegexOptions.IgnoreCase | RegexOptions.Multiline);
					if (match.Success) name = match.Value;
					match = Regex.Match(osRelease, @"(?<=^VERSION_ID\s*=\s*""?[^""0-9]*?)[0-9]+(\.[0-9]+)?(\.[0-9]+)?(\.[0-9]+)?", RegexOptions.IgnoreCase | RegexOptions.Multiline);
					Version osReleaseVersion;
					int intVersion;
					if (match.Success)
					{
						if (Version.TryParse(match.Value, out osReleaseVersion)) version = osReleaseVersion;
						else if (int.TryParse(match.Value, out intVersion)) {
							version = new Version(intVersion, 0);
						}
					}
				}
				if (name == null)
				{
					var osRelease = Shell.Standard.Exec("lsb_release -a").Output().Result;
					var match = Regex.Match(osRelease, "(?<=^Distributor ID\\s*:\\s*)[^\\s$]+(?=\\s|$)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
					if (match.Success) name = match.Value;
					match = Regex.Match(osRelease, @"(?<=^Release\s*:[^0-9]*?)[0-9]+\.[0-9]+(\.[0-9]+)?(\.[0-9]+)?", RegexOptions.IgnoreCase | RegexOptions.Multiline);
					if (match.Success) Version.TryParse(match.Value, out version);
				}
				// TODO use hostnamectl
				OSFlavor f;
				if (name == null) flavor = OSFlavor.Other;
				else
				{
					if (name != "Linux" && name != "linux" && name.EndsWith("linux", StringComparison.OrdinalIgnoreCase))
					{
						name = name.Substring(0, name.Length - "linux".Length);
					}
					if (Enum.TryParse<OSFlavor>(name, out f)) flavor = f;
					else
					{
						for (var os = OSFlavor.Min; os <= OSFlavor.Max; os++)
						{
							if (Regex.IsMatch(name, $"(?<=^|\\s){Regex.Escape(Enum.GetName(typeof(OSFlavor), os))}(?=\\s|$)", RegexOptions.IgnoreCase))
							{
								flavor = os;
								break;
							}
                            }
                            for (var os = OSFlavor.Min; os <= OSFlavor.Max; os++)
                            {
                                if (name.IndexOf(Enum.GetName(typeof(OSFlavor), os)) >= 0)
                                {
                                    flavor = os;
                                    break;
                                }
                            }
                        }
                    }
			}
			return flavor == OSFlavor.Unknown ? flavor = OSFlavor.Other : flavor;
		}
	}
	public static Version OSVersion
	{
		get
		{
			var flavor = OSFlavor;
			return version;
		}
	}

	public static string HostPanelProVersion
	{
		get
		{
			var entryAssemblies = new string[] { "HostPanelPro.WebPortal","HostPanelPro.EnterpriseServer", "HostPanelPro.Server", "HostPanelPro.WebDavPortal" };
			var entryAssembly = AppDomain.CurrentDomain.GetAssemblies()
				.FirstOrDefault(a => entryAssemblies.Any(name => a.GetName().Name == name)) ?? Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
			var fileVersion = entryAssembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
                if (fileVersion == null) return entryAssembly.GetName().Version.ToString();
			else return fileVersion.Version;
		}
	}
	public static WindowsVersion WindowsVersion => IsWindows ? WindowsOSInfo.GetVersion() : WindowsVersion.NonWindows;
	public static string Description
	{
		get
		{
			if (IsLinux)
			{
				string name = null;
				const string OsReleaseFile = "/etc/os-release";
				if (File.Exists(OsReleaseFile))
				{
					var osRelease = File.ReadAllText(OsReleaseFile);
					var match = Regex.Match(osRelease, "(?<=^PRETTY_NAME\\s*=\\s*\")[^\"]+(?=\")", RegexOptions.IgnoreCase | RegexOptions.Multiline);
					if (match.Success) name = match.Value;
				}
				if (name == null)
				{
					var osRelease = Shell.Standard.Exec("lsb_release -a").Output().Result;
					var match = Regex.Match(osRelease, "(?<=^Description\\s*:\\s*)[^\\s$]+(?=\\s|$)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
					if (match.Success) name = match.Value;
				}
				if (name == null)
				{
					name = Shell.Standard.Exec("uname").Output().Result;
				}
				return name;

			}
			else if (IsWindows)
			{
				switch (WindowsVersion)
				{
					case WindowsVersion.WindowsServer2025:
						return "Windows Server 2025";
					case WindowsVersion.WindowsServer2022:
						return "Windows Server 2022";
					case WindowsVersion.Windows11:
						return "Windows 11";
					case WindowsVersion.Windows10:
						return "Windows 10";
					case WindowsVersion.WindowsServer2019:
						return "Windows Server 2019";
					case WindowsVersion.WindowsServer2016:
						return "Windows Server 2016";
					case WindowsVersion.WindowsServer2012R2:
						return "Windows Server 2012 R2";
					case WindowsVersion.WindowsServer2012:
						return "Windows Server 2012";
					case WindowsVersion.Windows8:
						return "Windows 8";
					case WindowsVersion.Windows81:
						return "Windows 8.1";
					case WindowsVersion.Vista:
						return "Windows Vista";
					case WindowsVersion.Windows7:
						return "Windows 7";
					case WindowsVersion.WindowsServer2008R2:
						return "Windows Server 2008 R2";
					case WindowsVersion.WindowsServer2008:
						return "Windows Server 2008";
					case WindowsVersion.WindowsServer2003:
						return "Windows Server 2003";
					case WindowsVersion.WindowsXP:
						return "Windows XP";
					default:
						return RuntimeInformation.OSDescription;
				}
			}
			else
			{
				return RuntimeInformation.OSDescription;
			}
		}
	}

	public static OS.IUnixOperatingSystem Unix => (IUnixOperatingSystem)Current;
	public static IWindowsOperatingSystem Windows => (IWindowsOperatingSystem)Current;

	static Providers.OS.IOperatingSystem os = null;
	public static Providers.OS.IOperatingSystem Current
	{
		get
		{
			if (os == null)
			{
				if (IsWindows)
				{
					var version = WindowsOSInfo.GetVersion();
					switch (version)
					{
						case WindowsVersion.WindowsServer2025:
							os = Activator.CreateInstance(TypeResolver.GetType("HostPanelPro.Providers.OS.Windows2025, HostPanelPro.Providers.OS.Windows")) as Providers.OS.IOperatingSystem;
							break;
						case WindowsVersion.WindowsServer2022:
						case WindowsVersion.Windows11:
							os = Activator.CreateInstance(TypeResolver.GetType("HostPanelPro.Providers.OS.Windows2022, HostPanelPro.Providers.OS.Windows")) as Providers.OS.IOperatingSystem;
							break;
						case WindowsVersion.Windows10:
						case WindowsVersion.WindowsServer2019:
							os = Activator.CreateInstance(TypeResolver.GetType("HostPanelPro.Providers.OS.Windows2019, HostPanelPro.Providers.OS.Windows")) as Providers.OS.IOperatingSystem;
							break;
						case WindowsVersion.WindowsServer2016:
							os = Activator.CreateInstance(TypeResolver.GetType("HostPanelPro.Providers.OS.Windows2016, HostPanelPro.Providers.OS.Windows")) as Providers.OS.IOperatingSystem;
							break;
						case WindowsVersion.WindowsServer2012:
						case WindowsVersion.Windows8:
						case WindowsVersion.WindowsServer2012R2:
						case WindowsVersion.Windows81:
							os = Activator.CreateInstance(TypeResolver.GetType("HostPanelPro.Providers.OS.Windows2012, HostPanelPro.Providers.OS.Windows")) as Providers.OS.IOperatingSystem;
							break;
						case WindowsVersion.WindowsServer2008:
						case WindowsVersion.WindowsServer2008R2:
						case WindowsVersion.Vista:
						case WindowsVersion.Windows7:
							os = Activator.CreateInstance(TypeResolver.GetType("HostPanelPro.Providers.OS.Windows2008, HostPanelPro.Providers.OS.Windows")) as Providers.OS.IOperatingSystem;
							break;

						case WindowsVersion.WindowsServer2003:
						case WindowsVersion.WindowsXP:
						case WindowsVersion.WindowsNT4:
							os = Activator.CreateInstance(TypeResolver.GetType("HostPanelPro.Providers.OS.Windows2003, HostPanelPro.Providers.OS.Windows")) as Providers.OS.IOperatingSystem;
							break;
					}
				}
				else if (IsUnix)
				{
					os = Activator.CreateInstance(TypeResolver.GetType("HostPanelPro.Providers.OS.Unix, HostPanelPro.Providers.OS.Unix")) as Providers.OS.IOperatingSystem;
				}
			}
			return os;
		}
	}
}
