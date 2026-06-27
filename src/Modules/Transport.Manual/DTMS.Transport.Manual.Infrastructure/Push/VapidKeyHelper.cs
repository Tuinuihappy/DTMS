using WebPush;

namespace DTMS.Transport.Manual.Infrastructure.Push;

// Phase 4.3 — One-shot helper so an ops user can generate VAPID keys
// without leaving the project. Invoked via:
//
//   dotnet run --project src/DTMS.Api -- --generate-vapid-keys
//
// (wired in Program.cs near the top). Prints to stdout — operator
// pastes into appsettings.Development.json or .env. Public key is NOT
// secret; private key IS — keep it out of version control.
public static class VapidKeyHelper
{
    public static (string PublicKey, string PrivateKey) Generate()
    {
        var keys = VapidHelper.GenerateVapidKeys();
        return (keys.PublicKey, keys.PrivateKey);
    }
}
