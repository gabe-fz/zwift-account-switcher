using System.ComponentModel;
using System.Runtime.InteropServices;
using ZwiftAccountSwitcher.Core.Secrets;

namespace ZwiftAccountSwitcher.Windows.Secrets;

public sealed class WindowsCredentialStore : ICredentialStore
{
    private const uint CredentialTypeGeneric = 1;
    private const uint PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaximumPasswordCharacters = 512;

    public Task StoreAsync(
        Guid accountId,
        string username,
        ReadOnlyMemory<char> password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWindows();
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("An account ID is required.", nameof(accountId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        if (password.IsEmpty || password.Length > MaximumPasswordCharacters)
        {
            throw new ArgumentException($"The password must contain between 1 and {MaximumPasswordCharacters} characters.", nameof(password));
        }

        var passwordCopy = password.ToArray();
        var blob = Marshal.AllocHGlobal(passwordCopy.Length * sizeof(char));
        try
        {
            Marshal.Copy(passwordCopy, 0, blob, passwordCopy.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = GetTarget(accountId),
                CredentialBlobSize = checked((uint)(passwordCopy.Length * sizeof(char))),
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = username,
            };

            if (!CredWrite(ref credential, 0))
            {
                throw CreateException("Windows Credential Manager could not save the credential.");
            }
        }
        finally
        {
            Array.Clear(passwordCopy);
            ZeroAndFree(blob, passwordCopy.Length * sizeof(char));
        }

        return Task.CompletedTask;
    }

    public Task<StoredCredential?> RetrieveAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWindows();
        if (!CredRead(GetTarget(accountId), CredentialTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return Task.FromResult<StoredCredential?>(null);
            }

            throw CreateException("Windows Credential Manager could not read the credential.", error);
        }

        try
        {
            var native = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (native.CredentialBlobSize % sizeof(char) != 0)
            {
                throw new CredentialStoreException("Windows Credential Manager returned an invalid credential.");
            }

            var characterCount = checked((int)native.CredentialBlobSize / sizeof(char));
            var password = new char[characterCount];
            if (characterCount > 0)
            {
                Marshal.Copy(native.CredentialBlob, password, 0, characterCount);
            }

            var username = native.UserName ?? string.Empty;
            return Task.FromResult<StoredCredential?>(new StoredCredential(username, password));
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public Task DeleteAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWindows();
        if (!CredDelete(GetTarget(accountId), CredentialTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
            {
                throw CreateException("Windows Credential Manager could not delete the credential.", error);
            }
        }

        return Task.CompletedTask;
    }

    private static string GetTarget(Guid accountId) => $"ZwiftAccountSwitcher/account/{accountId:N}";

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows Credential Manager is only available on Windows.");
        }
    }

    private static CredentialStoreException CreateException(string message, int? error = null)
    {
        var code = error ?? Marshal.GetLastWin32Error();
        return new CredentialStoreException(message, new Win32Exception(code));
    }

    private static void ZeroAndFree(IntPtr pointer, int byteCount)
    {
        if (pointer == IntPtr.Zero)
        {
            return;
        }

        for (var index = 0; index < byteCount; index++)
        {
            Marshal.WriteByte(pointer, index, 0);
        }

        Marshal.FreeHGlobal(pointer);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
