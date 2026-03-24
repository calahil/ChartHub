using ChartHub.Services;

using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;

namespace ChartHub.Tests;

[Trait(TestInfrastructure.TestCategories.Category, TestInfrastructure.TestCategories.Unit)]
public sealed class GoogleDriveClientTests
{
    [Fact]
    public async Task GetServiceAsync_WhenCalledConcurrently_InitializesOnceUsingSilentCredential()
    {
        var authProvider = new CountingGoogleAuthProvider(returnSilentCredential: true);
        var sut = new GoogleDriveClient(authProvider);

        Task<DriveService>[] tasks = Enumerable.Range(0, 20)
            .Select(_ => sut.GetServiceAsync())
            .ToArray();

        DriveService[] services = await Task.WhenAll(tasks);

        Assert.NotEmpty(services);
        Assert.All(services, service => Assert.Same(services[0], service));
        Assert.Equal(1, authProvider.TryAuthorizeSilentCallCount);
        Assert.Equal(0, authProvider.AuthorizeInteractiveCallCount);
    }

    [Fact]
    public async Task GetServiceAsync_WhenSilentUnavailable_UsesInteractiveOnlyOnceUnderConcurrency()
    {
        var authProvider = new CountingGoogleAuthProvider(returnSilentCredential: false);
        var sut = new GoogleDriveClient(authProvider);

        Task<DriveService>[] tasks = Enumerable.Range(0, 20)
            .Select(_ => sut.GetServiceAsync())
            .ToArray();

        DriveService[] services = await Task.WhenAll(tasks);

        Assert.NotEmpty(services);
        Assert.All(services, service => Assert.Same(services[0], service));
        Assert.Equal(1, authProvider.TryAuthorizeSilentCallCount);
        Assert.Equal(1, authProvider.AuthorizeInteractiveCallCount);
    }

    [Fact]
    public async Task SignOutAsync_AfterServiceInitialization_ResetsStateAndReinitializesOnNextUse()
    {
        var authProvider = new CountingGoogleAuthProvider(returnSilentCredential: true);
        var sut = new GoogleDriveClient(authProvider);

        DriveService firstService = await sut.GetServiceAsync();
        Assert.Equal(1, authProvider.TryAuthorizeSilentCallCount);

        await sut.SignOutAsync();

        Assert.Equal(1, authProvider.SignOutCallCount);

        DriveService secondService = await sut.GetServiceAsync();

        Assert.Equal(2, authProvider.TryAuthorizeSilentCallCount);
        Assert.NotSame(firstService, secondService);
    }

    [Fact]
    public void GetUniqueFilePath_WhenSanitizedNameCollides_AppendsNumericSuffix()
    {
        using var temp = new TestInfrastructure.TemporaryDirectoryFixture("drive-helper-collision");
        string existingPath = Path.Combine(temp.RootPath, "setlist_.zip");
        File.WriteAllText(existingPath, "existing");

        string resolved = GoogleDriveFolderDownloadHelper.GetUniqueFilePath(temp.RootPath, "setlist_.zip");

        Assert.Equal(Path.Combine(temp.RootPath, "setlist_ (2).zip"), resolved);
    }

    [Fact]
    public void TryGetExportDescriptor_ForGoogleDocument_ReturnsPdfDescriptor()
    {
        bool resolved = GoogleDriveFolderDownloadHelper.TryGetExportDescriptor(
            "application/vnd.google-apps.document",
            out GoogleDriveExportDescriptor descriptor);

        Assert.True(resolved);
        Assert.Equal("application/pdf", descriptor.ExportMimeType);
        Assert.Equal(".pdf", descriptor.FileExtension);
    }

    private sealed class CountingGoogleAuthProvider(bool returnSilentCredential) : IGoogleAuthProvider
    {
        private readonly UserCredential _credential = CreateCredential();
        private int _tryAuthorizeSilentCallCount;
        private int _authorizeInteractiveCallCount;
        private int _signOutCallCount;

        public int TryAuthorizeSilentCallCount => _tryAuthorizeSilentCallCount;
        public int AuthorizeInteractiveCallCount => _authorizeInteractiveCallCount;
        public int SignOutCallCount => _signOutCallCount;

        public Task<UserCredential?> TryAuthorizeSilentAsync(IEnumerable<string> scopes, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _tryAuthorizeSilentCallCount);
            return Task.FromResult(returnSilentCredential ? _credential : null);
        }

        public Task<UserCredential> AuthorizeInteractiveAsync(IEnumerable<string> scopes, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _authorizeInteractiveCallCount);
            return Task.FromResult(_credential);
        }

        public Task SignOutAsync(UserCredential? credential, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _signOutCallCount);
            return Task.CompletedTask;
        }

        private static UserCredential CreateCredential()
        {
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = "test-client-id",
                    ClientSecret = "test-client-secret",
                },
                Scopes = [DriveService.Scope.DriveReadonly],
            });

            var token = new TokenResponse
            {
                AccessToken = "test-access-token",
                RefreshToken = "test-refresh-token",
                ExpiresInSeconds = 3600,
            };

            return new UserCredential(flow, "unit-test-user", token);
        }
    }
}
