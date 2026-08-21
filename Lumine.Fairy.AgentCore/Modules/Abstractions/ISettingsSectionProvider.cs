using Lumine.Fairy.Models;
using System.Threading;
using System.Threading.Tasks;

namespace Lumine.Fairy.Modules.Abstractions;

/// <summary>
/// 描述一个可动态挂载到宿主设置页中的设置分区提供器。
/// </summary>
public interface ISettingsSectionProvider
{
    /// <summary>
    /// 获取分区清单信息。
    /// </summary>
    SettingsSectionManifest Manifest { get; }

    /// <summary>
    /// 创建该分区使用的 ViewModel。
    /// </summary>
    /// <returns>分区 ViewModel 实例。</returns>
    object CreateViewModel();

    /// <summary>
    /// 创建该分区使用的视图对象。
    /// </summary>
    /// <returns>分区视图对象。</returns>
    object CreateView();

    /// <summary>
    /// 异步加载该分区的数据。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步保存该分区的数据。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SaveAsync(CancellationToken cancellationToken = default);
}