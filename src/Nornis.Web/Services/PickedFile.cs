namespace Nornis.Web.Services;

/// <summary>
/// A file the browser is holding on the page's behalf, described the way the upload
/// handshake needs it. Public and shared rather than private to the capture page because it
/// crosses the JS boundary: <c>nornisUpload.prepareImages</c> re-encodes a camera photo and
/// returns these, and the name, size and content type must describe the bytes that will
/// actually be PUT rather than the ones the camera produced.
/// </summary>
public record PickedFile(string Name, long Size, string Type);
