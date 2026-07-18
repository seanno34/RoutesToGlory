using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

namespace RoutesToGlory.EditorTools
{
    /// <summary>
    /// iOS 14+ blocks HTTP to LAN IPs (e.g. a local API at 192.168.x.x) unless
    /// NSLocalNetworkUsageDescription is present and the user grants Local Network access.
    /// Prefer production HTTPS (<c>https://8082ventures.com/rtg_api/api</c>) for off-LAN
    /// devices — that path needs no Local Network permission. Keep this injector for
    /// same-Wi‑Fi LAN field tests. Unity does not expose the plist keys in Player Settings.
    /// </summary>
    public static class RtgIosLocalNetworkPostProcess
    {
        private const string LocalNetworkUsageDescription =
            "Routes to Glory connects to your development server on the local network to load the Survey World map, routes, and fog of war.";

        // Declaring a Bonjour service type helps iOS authorize general LAN traffic.
        private const string BonjourServiceType = "_rtg-dev._tcp";

        [PostProcessBuild(50)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
#if UNITY_IOS
            if (target != BuildTarget.iOS) return;

            string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);

            plist.root.SetString("NSLocalNetworkUsageDescription", LocalNetworkUsageDescription);

            PlistElementArray bonjour = plist.root.CreateArray("NSBonjourServices");
            bonjour.AddString(BonjourServiceType);

            plist.WriteToFile(plistPath);
#endif
        }
    }
}
