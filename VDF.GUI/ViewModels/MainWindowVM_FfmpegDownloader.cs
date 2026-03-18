// /*
//     Copyright (C) 2025 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
//     You should have received a copy of the GNU Affero General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Collections.Generic;
using System.Net.Http;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Threading;
using FFmpeg.AutoGen;
using ReactiveUI;
using VDF.Core.FFTools;
using VDF.Core.Utils;
using System.Reactive;
using VDF.GUI.Views;

namespace VDF.GUI.ViewModels {
	public partial class MainWindowVM : ReactiveObject {
		bool _isFfmpegDownloadInProgress;
		public bool IsFfmpegDownloadInProgress {
			get => _isFfmpegDownloadInProgress;
			set => this.RaiseAndSetIfChanged(ref _isFfmpegDownloadInProgress, value);
		}

		public ReactiveCommand<Unit, Unit> DownloadSharedFfmpegCommand => ReactiveCommand.CreateFromTask(async () => {
			await DownloadSharedFfmpegAsync();
		});

		async Task DownloadSharedFfmpegAsync() {
			if (IsFfmpegDownloadInProgress) return;
			IsFfmpegDownloadInProgress = true;
			IsBusy = true;
			IsBusyOverlayText = App.Lang["Message.FfmpegDownloadPreparing"];
			string? errorMessage = null;
			string? extractedFolder = null;
			string? targetFolder = null;
			bool downloadSucceeded = false;

			try {
				var plans = GetSharedFfmpegDownloadPlans();
				if (plans.Count == 0) {
					errorMessage = App.Lang["Message.FfmpegDownloadUnsupported"];
					await MessageBoxService.Show(BuildFfmpegInstallInstructions(false, false, null, errorMessage));
					return;
				}

				foreach (var plan in plans) {
					string tempRoot = Path.Combine(Path.GetTempPath(), "VDF.FFmpegDownload");
					string downloadPath = Path.Combine(tempRoot, plan.ArchiveFileName);
					extractedFolder = Path.Combine(tempRoot, "extracted");

					if (!Directory.Exists(tempRoot)) Directory.CreateDirectory(tempRoot);
					if (Directory.Exists(extractedFolder))
						Directory.Delete(extractedFolder, true);
					Directory.CreateDirectory(extractedFolder);

					try {
						await DownloadFileAsync(plan.DownloadUrl, downloadPath, plan);
						IsBusyOverlayText = App.Lang["Message.FfmpegDownloadExtracting"];
						ExtractArchive(downloadPath, extractedFolder, plan.ArchiveKind);

						// CRITICAL: Move to AppData/Application Support instead of App Folder
						targetFolder = Path.Combine(DatabaseUtils.BaseDatabaseFolder, "bin");
						if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);
						
						CopyFfmpegFiles(extractedFolder, targetFolder);
						downloadSucceeded = true;
						break;
					}
					catch (Exception ex) {
						errorMessage = string.Format(CultureInfo.InvariantCulture, App.Lang["Message.FfmpegDownloadFailed"], ex.Message);
					}
				}

				bool ffmpegFound = FFToolsUtils.GetPath(FFToolsUtils.FFTool.FFmpeg) != null;
				bool ffprobeFound = FFToolsUtils.GetPath(FFToolsUtils.FFTool.FFProbe) != null;
				
				await MessageBoxService.Show(BuildFfmpegInstallInstructions(ffmpegFound, ffprobeFound, targetFolder, downloadSucceeded ? null : errorMessage));
			}
			catch (Exception ex) {
				errorMessage = ex.Message;
				await MessageBoxService.Show(BuildFfmpegInstallInstructions(false, false, null, errorMessage));
			}
			finally {
				IsBusy = false;
				IsBusyOverlayText = string.Empty;
				IsFfmpegDownloadInProgress = false;
			}
		}

		record FfmpegDownloadPlan(Uri DownloadUrl, string ArchiveFileName, ArchiveType ArchiveKind, string DisplayName);

		enum ArchiveType { Zip, TarXz, TarGz }

		List<FfmpegDownloadPlan> GetSharedFfmpegDownloadPlans() {
			var plans = new List<FfmpegDownloadPlan>();
			Architecture arch = RuntimeInformation.ProcessArchitecture;
			
			string github = "https://github.com";
			string btbnRepo = "BtbN/FFmpeg-Builds";
			string ytdlpRepo = "yt-dlp/FFmpeg-Builds";
			string releasePath = "releases/download/latest";

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				string winBase = $"{github}/{btbnRepo}/{releasePath}";
				if (arch == Architecture.X64)
					plans.Add(new FfmpegDownloadPlan(new Uri($"{winBase}/ffmpeg-master-latest-win64-gpl-shared.zip"), "ffmpeg-win64.zip", ArchiveType.Zip, "Windows x64"));
				else if (arch == Architecture.Arm64)
					plans.Add(new FfmpegDownloadPlan(new Uri($"{winBase}/ffmpeg-master-latest-winarm64-gpl-shared.zip"), "ffmpeg-winarm64.zip", ArchiveType.Zip, "Windows ARM64"));
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
				string linuxBase = $"{github}/{btbnRepo}/{releasePath}";
				if (arch == Architecture.X64)
					plans.Add(new FfmpegDownloadPlan(new Uri($"{linuxBase}/ffmpeg-master-latest-linux64-gpl-shared.tar.xz"), "ffmpeg-linux64.tar.xz", ArchiveType.TarXz, "Linux x64"));
				else if (arch == Architecture.Arm64)
					plans.Add(new FfmpegDownloadPlan(new Uri($"{linuxBase}/ffmpeg-master-latest-linuxarm64-gpl-shared.tar.xz"), "ffmpeg-linuxarm64.tar.xz", ArchiveType.TarXz, "Linux ARM64"));
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
				string macBase = $"{github}/{ytdlpRepo}/{releasePath}";
				if (arch == Architecture.Arm64)
					plans.Add(new FfmpegDownloadPlan(new Uri($"{macBase}/ffmpeg-master-latest-macosarm64-gpl-shared.tar.xz"), "ffmpeg-macosarm64.tar.xz", ArchiveType.TarXz, "macOS ARM64"));
				else
					plans.Add(new FfmpegDownloadPlan(new Uri($"{macBase}/ffmpeg-master-latest-macos64-gpl-shared.tar.xz"), "ffmpeg-macos64.tar.xz", ArchiveType.TarXz, "macOS x64"));
			}
			return plans;
		}

		async Task DownloadFileAsync(Uri downloadUrl, string destinationPath, FfmpegDownloadPlan plan) {
			using var client = new HttpClient();
			client.DefaultRequestHeaders.Add("User-Agent", "VideoDuplicateFinder-Downloader");
			using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
			response.EnsureSuccessStatusCode();

			var totalBytes = response.Content.Headers.ContentLength;
			await using var sourceStream = await response.Content.ReadAsStreamAsync();
			await using var destinationStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

			var buffer = new byte[81920];
			long totalRead = 0;
			int read;
			while ((read = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0) {
				await destinationStream.WriteAsync(buffer.AsMemory(0, read));
				totalRead += read;
				UpdateDownloadProgress(plan.DisplayName, totalRead, totalBytes);
			}
		}

		void UpdateDownloadProgress(string displayName, long totalRead, long? totalBytes) =>
			Dispatcher.UIThread.Post(() => {
				double percent = (totalBytes ?? 0) > 0 ? totalRead / (double)totalBytes.Value * 100 : 0;
				IsBusyOverlayText = $"Downloading {displayName}: {Math.Round(percent, 1)}% ({FormatBytes(totalRead)})";
			});

		static string FormatBytes(long? bytes) {
			if (bytes == null) return "?";
			double size = bytes.Value;
			string[] units = { "B", "KB", "MB", "GB" };
			int unit = 0;
			while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
			return $"{size:0.##} {units[unit]}";
		}

		static void ExtractArchive(string archivePath, string targetFolder, ArchiveType type) {
			if (type == ArchiveType.Zip) {
				ZipFile.ExtractToDirectory(archivePath, targetFolder, true);
				return;
			}
			using var process = Process.Start(new ProcessStartInfo {
				FileName = "tar",
				Arguments = $"-xf \"{archivePath}\" -C \"{targetFolder}\"",
				UseShellExecute = false, CreateNoWindow = true
			});
			process?.WaitForExit();
		}

		static void CopyFfmpegFiles(string sourceRoot, string targetFolder) {
			string[] libraryPrefixes = { "avcodec", "avformat", "avutil", "swresample", "swscale" };
			string ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".dll" : 
			             RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib" : ".so";

			foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)) {
				string name = Path.GetFileName(file);
				bool isBinary = name.StartsWith("ffmpeg") || name.StartsWith("ffprobe");
				bool isLib = libraryPrefixes.Any(p => name.StartsWith(p)) && name.Contains(ext);

				if (isBinary || isLib) {
					File.Copy(file, Path.Combine(targetFolder, name), true);
				}
			}
		}

		static string BuildFfmpegInstallInstructions(bool ffmpegFound, bool ffprobeFound, string? targetFolder, string? errorMessage) {
			var sb = new StringBuilder();
			if (!string.IsNullOrEmpty(errorMessage)) sb.AppendLine($"Error: {errorMessage}\n");
			sb.AppendLine(ffmpegFound && ffprobeFound ? "FFmpeg binaries verified!" : "FFmpeg binaries missing.");
			if (targetFolder != null) sb.AppendLine($"\nLocation: {targetFolder}");
			return sb.ToString();
		}
	}
}
