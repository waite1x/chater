using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Chater.Models;
using Chater.ViewModels;

namespace Chater.Views.Settings;

public partial class SkillSettingsView : UserControl
{
    public SkillSettingsView()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(SkillList, true);
        SkillList.AddHandler(InputElement.PointerPressedEvent, OnSkillPointerPressed, RoutingStrategies.Bubble, true);
    }

    private async void OnSkillPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(SkillList).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
        {
            return;
        }

        var item = e.Source as ListBoxItem ?? (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>();
        if (item?.DataContext is not Skill skill)
        {
            return;
        }

        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(skill.Id));
        DragPreviewName.Text = skill.Name;
        DragPreviewDescription.Text = skill.Description;
        DragPreview.IsVisible = true;
        UpdateDragPreviewPosition(e.GetPosition(this));
        try
        {
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
        finally
        {
            DragPreview.IsVisible = false;
        }
    }

    private void OnSkillDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.TryGetText() is not null ? DragDropEffects.Move : DragDropEffects.None;
        UpdateDragPreviewPosition(e.GetPosition(this));
        e.Handled = true;
    }

    private async void OnSkillDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || e.DataTransfer.TryGetText() is not string draggedId)
        {
            return;
        }

        var draggedSkill = viewModel.Skills.FirstOrDefault(skill => skill.Id == draggedId);
        if (draggedSkill is null)
        {
            return;
        }

        var targetItem = (SkillList.InputHitTest(e.GetPosition(SkillList)) as Visual)?.FindAncestorOfType<ListBoxItem>();
        var targetSkill = targetItem?.DataContext as Skill;
        var insertAfter = targetItem is not null && e.GetPosition(targetItem).Y > targetItem.Bounds.Height / 2;
        await viewModel.ReorderSkillsAsync(draggedSkill, targetSkill, insertAfter);
        e.Handled = true;
    }

    private void UpdateDragPreviewPosition(Point point)
    {
        Canvas.SetLeft(DragPreview, Math.Clamp(point.X + 12, 0, Math.Max(0, Bounds.Width - DragPreview.Bounds.Width)));
        Canvas.SetTop(DragPreview, Math.Clamp(point.Y + 12, 0, Math.Max(0, Bounds.Height - DragPreview.Bounds.Height)));
    }
}
