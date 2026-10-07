using System.Runtime.InteropServices;
namespace CfcPilot;

public static class DeviceDiagnostics
{
    [DllImport("winscard.dll")] static extern int SCardEstablishContext(uint scope,IntPtr reserved1,IntPtr reserved2,out IntPtr context);
    [DllImport("winscard.dll",CharSet=CharSet.Unicode)] static extern int SCardListReadersW(IntPtr context,string? groups,[Out] char[]? readers,ref uint length);
    [DllImport("winscard.dll")] static extern int SCardReleaseContext(IntPtr context);
    public static object Read()
    {
        var readers=new List<string>();var detail="Disponível somente no computador Windows que executa o app.";
        if(OperatingSystem.IsWindows())
        {
            var status=SCardEstablishContext(0,IntPtr.Zero,IntPtr.Zero,out var context);
            if(status==0)try
            {
                uint length=0;status=SCardListReadersW(context,null,null,ref length);
                if(status==0 && length is >0 and <32768){var buffer=new char[length];status=SCardListReadersW(context,null,buffer,ref length);if(status==0)readers.AddRange(new string(buffer).Split('\0',StringSplitOptions.RemoveEmptyEntries));}
                detail=readers.Count>0?"Leitor PC/SC detectado. Seleção do certificado e PIN ocorrem no navegador/middleware.":"Nenhum leitor PC/SC disponível neste computador. Conecte o leitor e confira o driver e o serviço Cartão Inteligente.";
            }
            finally{SCardReleaseContext(context);}
            else detail="O serviço de cartão inteligente não respondeu. Código: 0x"+unchecked((uint)status).ToString("X8")+".";
        }
        var roots=new[]{Directory.GetCurrentDirectory(),@"C:\InforCFC\CFCB",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"NITGEN"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"NITGEN")};
        var sdk=roots.Where(Directory.Exists).SelectMany(root=>new[]{"NBioBSP.dll","NBioBSPCOM.dll","NBioBSPJNI.dll"}.Select(file=>Path.Combine(root,file))).Where(File.Exists).ToArray();
        return new{biometric=new{reportedModel="Hamster 3",reference="Nitgen Fingkey Hamster III / HFDU06S — confirmar etiqueta e driver",sdkDetected=sdk.Length>0,sdkFiles=sdk.Select(Path.GetFileName),status="Captura e homologação do fluxo e-CNH pendentes"},smartCard=new{readers,detail},checkedAt=DateTimeOffset.UtcNow};
    }
}
