using PzlEv.Modules.Import.Models;

namespace PzlEv.Modules.Import.ViewModels;

/// <summary>Okno logowania do SharePoint (MS-OFBA) – ViewModel nie otwiera okien sam.</summary>
public interface ISharePointLoginDialog
{
    /// <summary>true – logowanie zakończone (osiągnięty adres powrotu / strona witryny albo „Gotowe”).</summary>
    bool Login(SharePointLoginRequest request);
}
