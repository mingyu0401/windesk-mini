using System.Threading.Tasks;

namespace WindeskMini11
{
    /// <summary>页面与主窗口之间的交互契约。</summary>
    public interface IPageHost
    {
        void ShowToast(string message, bool success = true);

        bool TryRegisterHotkey(out string error);

        Task RestartExplorerAsync();

        Task DeepRefreshAsync();

        void RefreshAllPages();
    }

    /// <summary>页面状态刷新。</summary>
    public interface IRefreshablePage
    {
        void RefreshState();
    }
}
