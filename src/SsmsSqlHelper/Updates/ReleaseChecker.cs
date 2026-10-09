using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace SsmsSqlHelper.Updates
{
    internal static class ReleaseChecker
    {
        public const string ReleasesUrl = "https://github.com/jirakitc/SSMS-SQoL/releases";
        public const string ApiUrl = "https://api.github.com/repos/jirakitc/SSMS-SQoL/releases?per_page=30";

        private static readonly Regex InstallerName = new Regex(
            @"^SsmsSqlHelper-(?<version>\d+\.\d+\.\d+(?:\.\d+)?)(?:-[0-9A-Za-z.-]+)?\.(?:zip|vsix)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static Version LatestInstallerVersion(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("GitHub returned an empty response.", nameof(json));

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var releases = (GitHubRelease[])new DataContractJsonSerializer(typeof(GitHubRelease[])).ReadObject(stream);
                Version latest = null;
                foreach (var release in releases ?? Array.Empty<GitHubRelease>())
                {
                    if (release == null || release.Draft)
                        continue;

                    var installerVersions = (release.Assets ?? Array.Empty<GitHubAsset>())
                        .Select(asset => asset == null ? null : InstallerName.Match(asset.Name ?? ""))
                        .Where(match => match != null && match.Success)
                        .Select(match => ParseVersion(match.Groups["version"].Value))
                        .Where(version => version != null)
                        .ToArray();

                    // A versioned installer is the source of truth. Some older releases have
                    // a tag that differs from the VSIX inside the ZIP.
                    var version = installerVersions.Length > 0
                        ? installerVersions.Max()
                        : ParseTag(release.TagName);

                    if (version != null && (latest == null || version.CompareTo(latest) > 0))
                        latest = version;
                }
                return latest;
            }
        }

        private static Version ParseTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return null;
            var value = tag.Trim().TrimStart('v', 'V');
            var suffix = value.IndexOfAny(new[] { '-', '+' });
            if (suffix >= 0)
                value = value.Substring(0, suffix);
            return ParseVersion(value);
        }

        private static Version ParseVersion(string text)
        {
            return Version.TryParse(text, out var version) && version.Major >= 0 &&
                   version.Minor >= 0 && version.Build >= 0 ? version : null;
        }

        [DataContract]
        private sealed class GitHubRelease
        {
            [DataMember(Name = "tag_name")]
            public string TagName { get; set; }

            [DataMember(Name = "draft")]
            public bool Draft { get; set; }

            [DataMember(Name = "assets")]
            public GitHubAsset[] Assets { get; set; }
        }

        [DataContract]
        private sealed class GitHubAsset
        {
            [DataMember(Name = "name")]
            public string Name { get; set; }
        }
    }
}
