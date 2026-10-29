namespace Mahjong.Core.Content;

/// <summary>
/// 内容加载或校验失败。<see cref="Path"/> 是出问题的文件（或目录）的绝对路径，消息里也带该路径，
/// 便于启动日志直接定位。
/// </summary>
public sealed class ContentLoadException : Exception
{
    public ContentLoadException(string path, string reason)
        : base($"Content load failed for '{path}': {reason}")
    {
        Path = path;
    }

    public ContentLoadException(string path, string reason, Exception innerException)
        : base($"Content load failed for '{path}': {reason}", innerException)
    {
        Path = path;
    }

    /// <summary>出错内容文件的绝对路径；无法解析路径时是传入的原始值。</summary>
    public string Path { get; }
}
