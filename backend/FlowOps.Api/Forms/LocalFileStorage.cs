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

    // The stored display name: client directories removed and length capped, keeping the extension.
    public static string CleanFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        var extension = Path.GetExtension(name).ToLowerInvariant();
        return name.Length > 180 ? name[..(180 - extension.Length)] + extension : name;
    }

    public static async Task<string> HashAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    public async Task<FormAttachment> SaveAsync(IFormFile file, FormField field, Guid organizationId, Guid submissionId, CancellationToken cancellationToken)
    {
        var name = CleanFileName(file.FileName);
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var limit = (field.MaxFileSizeMb ?? 10) * 1024L * 1024;
        if (file.Length <= 0) throw new InvalidDataException($"{field.Label}: Choose a non-empty file.");
        if (file.Length > limit) throw new InvalidDataException($"{field.Label}: File must be {field.MaxFileSizeMb ?? 10} MB or smaller.");
        if (field.AcceptedExtensions?.Contains(extension) != true || !FormFieldValidation.AllowedFileExtensions.Contains(extension))
            throw new InvalidDataException($"{field.Label}: Choose an allowed file type.");
        if (name.Any(char.IsControl)) throw new InvalidDataException($"{field.Label}: File name contains invalid characters.");
        var attachment = new FormAttachment { OrganizationId = organizationId, SubmissionId = submissionId, FieldId = field.Id, FileName = name, Sha256 = "" };
        var path = PathFor(attachment);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var content = new ContentCheck(extension);
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
                    content.Append(buffer.AsSpan(0, count));
                    await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                }
            }
            if (attachment.Size == 0 || attachment.Size != file.Length) throw new InvalidDataException($"{field.Label}: Incomplete file upload.");
            if (!content.Complete()) throw new InvalidDataException($"{field.Label}: File contents do not match its allowed type.");
            attachment.Sha256 = Convert.ToHexString(hash.GetHashAndReset());
            return attachment;
        }
        catch { Delete(attachment); throw; }
    }

    public void Delete(FormAttachment attachment)
    {
        var path = PathFor(attachment);
        File.Delete(path);
        try
        {
            var directory = Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
        catch (IOException) { }
    }

    internal static bool MatchesType(byte[] bytes, string extension)
    {
        var content = new ContentCheck(extension);
        content.Append(bytes);
        return content.Complete();
    }

    // Validates content incrementally while a file streams to disk: a signature prefix for
    // PDF/PNG/JPEG, strict UTF-8 without NUL bytes for TXT/CSV.
    private sealed class ContentCheck(string extension)
    {
        private static readonly byte[] Pdf = "%PDF-"u8.ToArray();
        private static readonly byte[] Png = [137, 80, 78, 71, 13, 10, 26, 10];
        private static readonly byte[] Jpeg = [255, 216, 255];
        private readonly byte[] header = new byte[8];
        private int headerLength;
        private readonly Decoder? text = extension is ".txt" or ".csv" ? new UTF8Encoding(false, true).GetDecoder() : null;
        private readonly char[] chars = new char[4096];
        private bool valid = extension is ".pdf" or ".png" or ".jpg" or ".jpeg" or ".txt" or ".csv";

        public void Append(ReadOnlySpan<byte> chunk)
        {
            if (!valid) return;
            var take = Math.Min(chunk.Length, header.Length - headerLength);
            chunk[..take].CopyTo(header.AsSpan(headerLength));
            headerLength += take;
            if (text is null) return;
            if (chunk.Contains((byte)0)) { valid = false; return; }
            valid = Decode(chunk, flush: false);
        }

        // Convert (not GetCharCount) keeps decoder state, so multi-byte characters may span chunks.
        private bool Decode(ReadOnlySpan<byte> bytes, bool flush)
        {
            try
            {
                do
                {
                    text!.Convert(bytes, chars, flush, out var used, out _, out _);
                    bytes = bytes[used..];
                } while (!bytes.IsEmpty);
                return true;
            }
            catch (DecoderFallbackException) { return false; }
        }

        public bool Complete()
        {
            if (!valid) return false;
            var start = header.AsSpan(0, headerLength);
            if (text is not null) return Decode([], flush: true);
            return extension switch
            {
                ".pdf" => start.StartsWith(Pdf),
                ".png" => start.StartsWith(Png),
                _ => start.StartsWith(Jpeg)
            };
        }
    }
}
