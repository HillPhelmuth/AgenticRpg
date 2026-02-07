using AgenticRpg.Core.Models;
using Microsoft.AspNetCore.Components;

namespace AgenticRpg.Client.Pages;
public partial class RpgModalMenu
{
    [Parameter]
    public Campaign? SelectedCampaign { get; set; }
    [Parameter]
    public string? CurrentUserId { get; set; }
    [Parameter]
    public string? SessionOrCampaignId { get; set; }
    [Parameter]
    public EventCallback OnCloseModalMenu { get; set; }
    public void CloseModal() => OnCloseModalMenu.InvokeAsync();
}
