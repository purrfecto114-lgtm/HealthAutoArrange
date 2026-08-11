namespace HealthAutoArrange.Core
{
    public enum UpdateUiState
    {
        Idle = 0,
        Checking = 1,
        UpToDate = 2,
        Available = 3,
        Downloading = 4,
        Downloaded = 5,
        Failed = 6
    }

    public sealed class UpdateUiSnapshot
    {
        public UpdateUiState State { get; }
        public string CurrentVersion { get; }
        public string LatestVersion { get; }
        public string Detail { get; }
        public string DownloadedPath { get; }
        public float Progress01 { get; }
        public bool HasUpdate => State == UpdateUiState.Available || State == UpdateUiState.Downloading || State == UpdateUiState.Downloaded;
        public bool CanCheck => State != UpdateUiState.Checking && State != UpdateUiState.Downloading;
        public bool CanDownload => State == UpdateUiState.Available;

        public UpdateUiSnapshot(UpdateUiState state, string currentVersion, string latestVersion, string detail, string downloadedPath, float progress01 = 0f)
        {
            State = state;
            CurrentVersion = currentVersion ?? string.Empty;
            LatestVersion = latestVersion ?? string.Empty;
            Detail = detail ?? string.Empty;
            DownloadedPath = downloadedPath ?? string.Empty;
            Progress01 = progress01 < 0f ? 0f : (progress01 > 1f ? 1f : progress01);
        }
    }
}
