using System.Drawing.Imaging;

namespace CodexDiscordPresence;

internal sealed class DashboardPresenceImageSlot : IDisposable
{
    private static readonly HttpClient ImageClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly Image _fallbackImage;
    private readonly Action _invalidate;
    private readonly EventHandler _frameChangedHandler;
    private Image _currentImage;
    private Stream? _currentImageStream;
    private CancellationTokenSource? _loadCancellation;
    private string? _requestedReference;
    private int _requestVersion;
    private bool _disposed;

    public DashboardPresenceImageSlot(Image fallbackImage, Action invalidate)
    {
        _fallbackImage = fallbackImage ?? throw new ArgumentNullException(nameof(fallbackImage));
        _invalidate = invalidate ?? throw new ArgumentNullException(nameof(invalidate));
        _frameChangedHandler = (_, _) => _invalidate();
        _currentImage = fallbackImage;
    }

    public Image CurrentImage => _currentImage;

    public void SetReference(string? imageReference)
    {
        if (_disposed)
        {
            return;
        }

        if (string.Equals(_requestedReference, imageReference, StringComparison.Ordinal))
        {
            return;
        }

        _requestedReference = imageReference;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        var requestVersion = ++_requestVersion;

        if (string.IsNullOrWhiteSpace(imageReference))
        {
            ReplaceImage(_fallbackImage);
            return;
        }

        var localImage = TryLoadLocalImage(imageReference);
        if (localImage is not null)
        {
            ReplaceImage(localImage);
            return;
        }

        if (!Uri.TryCreate(imageReference, UriKind.Absolute, out var imageUri) ||
            (!string.Equals(imageUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(imageUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            ReplaceImage(_fallbackImage);
            return;
        }

        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        _ = LoadRemoteImageAsync(imageUri, requestVersion, cancellation);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;

        StopAnimation(_currentImage);
        if (!ReferenceEquals(_currentImage, _fallbackImage))
        {
            _currentImage.Dispose();
        }

        _currentImageStream?.Dispose();
        _currentImageStream = null;
        _currentImage = _fallbackImage;
    }

    private async Task LoadRemoteImageAsync(
        Uri imageUri,
        int requestVersion,
        CancellationTokenSource cancellation)
    {
        try
        {
            var bytes = await ImageClient.GetByteArrayAsync(imageUri, cancellation.Token);
            var stream = new MemoryStream(bytes, writable: false);
            Image image;
            try
            {
                image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            }
            catch
            {
                stream.Dispose();
                throw;
            }

            if (cancellation.IsCancellationRequested || requestVersion != _requestVersion)
            {
                image.Dispose();
                stream.Dispose();
                return;
            }

            ReplaceImage(new ImageResource(image, stream));
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (!cancellation.IsCancellationRequested && requestVersion == _requestVersion)
            {
                ReplaceImage(_fallbackImage);
            }
        }
        finally
        {
            if (requestVersion == _requestVersion && ReferenceEquals(_loadCancellation, cancellation))
            {
                cancellation.Dispose();
                _loadCancellation = null;
            }
        }
    }

    private void ReplaceImage(ImageResource resource)
    {
        if (_disposed)
        {
            resource.Dispose();
            return;
        }

        ReplaceImage(resource.Image, resource.Stream);
    }

    private void ReplaceImage(Image image, Stream? stream = null)
    {
        if (_disposed)
        {
            if (!ReferenceEquals(image, _fallbackImage))
            {
                image.Dispose();
            }

            stream?.Dispose();
            return;
        }

        var previousImage = _currentImage;
        var previousStream = _currentImageStream;
        StopAnimation(previousImage);

        _currentImage = image;
        _currentImageStream = stream;
        StartAnimation(_currentImage);

        if (!ReferenceEquals(previousImage, _fallbackImage))
        {
            previousImage.Dispose();
        }

        previousStream?.Dispose();
        _invalidate();
    }

    private void StartAnimation(Image image)
    {
        try
        {
            if (ImageAnimator.CanAnimate(image))
            {
                ImageAnimator.Animate(image, _frameChangedHandler);
            }
        }
        catch
        {
        }
    }

    private void StopAnimation(Image image)
    {
        try
        {
            if (ImageAnimator.CanAnimate(image))
            {
                ImageAnimator.StopAnimate(image, _frameChangedHandler);
            }
        }
        catch
        {
        }
    }

    private static ImageResource? TryLoadLocalImage(string imageReference)
    {
        var fileName = GetLocalFileName(imageReference);
        if (fileName is null)
        {
            return null;
        }

        var extension = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var candidateNames = new List<string>();

        if (extension.Length == 0)
        {
            candidateNames.Add($"{fileName}.gif");
            candidateNames.Add($"{fileName}.png");
        }
        else
        {
            candidateNames.Add(fileName);
        }

        if (!stem.StartsWith("rpc_", StringComparison.OrdinalIgnoreCase))
        {
            if (extension.Length == 0)
            {
                candidateNames.Add($"rpc_{fileName}.gif");
                candidateNames.Add($"rpc_{fileName}.png");
            }
            else
            {
                candidateNames.Add($"rpc_{fileName}");
            }
        }

        foreach (var candidateName in candidateNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "RpcArt", candidateName);
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                try
                {
                    var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
                    return new ImageResource(image, stream);
                }
                catch
                {
                    stream.Dispose();
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static string? GetLocalFileName(string imageReference)
    {
        if (Uri.TryCreate(imageReference, UriKind.Absolute, out var imageUri) &&
            (string.Equals(imageUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(imageUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            var remoteFileName = Path.GetFileName(imageUri.LocalPath);
            return string.IsNullOrWhiteSpace(remoteFileName) ? null : remoteFileName;
        }

        var fileName = Path.GetFileName(imageReference);
        return string.Equals(fileName, imageReference, StringComparison.Ordinal) &&
               !string.IsNullOrWhiteSpace(fileName)
            ? fileName
            : null;
    }

    private sealed class ImageResource : IDisposable
    {
        public ImageResource(Image image, Stream stream)
        {
            Image = image;
            Stream = stream;
        }

        public Image Image { get; }

        public Stream Stream { get; }

        public void Dispose()
        {
            Image.Dispose();
            Stream.Dispose();
        }
    }
}
