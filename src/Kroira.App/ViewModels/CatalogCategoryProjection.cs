#nullable enable

namespace Kroira.App.ViewModels
{
    internal readonly record struct CatalogCategoryProjection(
        string RawCategoryName,
        string DisplayCategoryName,
        string DisplayCategoryKey);
}
