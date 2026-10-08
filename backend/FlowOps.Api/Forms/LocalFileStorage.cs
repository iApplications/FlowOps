using System.Security.Cryptography;
using System.Text;

namespace FlowOps.Api.Forms;

public sealed class LocalFileStorage
{
    public const long MaxTotalBytes = 20 * 1024 * 1024;
    public const long MaxRequestBytes = MaxTotalBytes + 1024 * 1024;
    private readonly string root;
    public LocalFileStorage(IConfiguration configuration, IWebHostEnvironment environment)
    {
        root = Path.GetFullPath(configuration["Storage:RootPath"] ?? Path.Combine(
            environment.IsDevelopment() ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) : environment.ContentRootPath,
            environment.IsDevelopment() ? "FlowOps" : "App_Data", "Uploads"));
        var webRoot = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
        if (root.Equals(webRoot, StringComparison.OrdinalIgnoreCase) || root.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Upload storage must be outside the public web folder.");
    }

    public string PathFor(FormAttachment attachment) => Path.Combine(root, attachment.OrganizationId.ToString("N"), attachment.SubmissionId.ToString("N"), attachment.Id.ToString("N") + ".bin");

    public async Task<FormAttachment> SaveAsync(IFormFile file, FormField field, Guid organizationId, Guid submissionId, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var limit = (field.MaxFileSizeMb ?? 10) * 1024L * 1024;
        if (file.Length <= 0) throw new InvalidDataException($"{field.Label}: Choose a non-empty file.");
        if (file.Length > limit) throw new InvalidDataException($"{field.Label}: File must be {field.MaxFileSizeMb ?? 10} MB or smaller.");
        if (field.AcceptedExtensions?.Contains(extension) != true || extension is not (".pdf" or ".png" or ".jpg" or ".jpeg" or ".txt" or ".csv"))
            throw new InvalidDataException($"{field.Label}: Choose an allowed file type.");
        if (name.Any(char.IsControl)) throw new InvalidDataException($"{field.Label}: File name contains invalid characters.");
        if (name.Length > 180) name = name[..(180 - extension.Length)] + extension;
        var attachment = new FormAttachment { OrganizationId = organizationId, SubmissionId = submissionId, FieldId = field.Id, FileName = name, Sha256 = "" };
        var path = PathFor(attachment);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = file.OpenReadStream())
            await using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    attachment.Size += count;
                    if (attachment.Size > limit) throw new InvalidDataException($"{field.Label}: File is too large.");
                    hash.AppendData(buffer, 0, count);
                    await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                }
            }
            if (attachment.Size == 0 || attachment.Size != file.Length) throw new InvalidDataException($"{field.Label}: Incomplete file upload.");
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            if (!MatchesType(bytes, extension)) throw new InvalidDataException($"{field.Label}: File contents do not match its allowed type.");
            attachment.Sha256 = Convert.ToHexString(hash.GetHashAndReset());
            return attachment;
        }
        catch { Delete(attachment); throw; }
    }

    public void Delete(FormAttachment attachment) => File.Delete(PathFor(attachment));

    internal static bool MatchesType(byte[] bytes, string extension)
    {
        if (extension == ".pdf") return bytes.AsSpan().StartsWith("%PDF-"u8);
        if (extension == ".png") return bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        if (extension is ".jpg" or ".jpeg") return bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 });
        if (extension is not (".txt" or ".csv") || bytes.Contains((byte)0)) return false;
        try { new UTF8Encoding(false, true).GetString(bytes); return true; }
        catch (DecoderFallbackException) { return false; }
    }
}
