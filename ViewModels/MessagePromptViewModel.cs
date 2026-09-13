using QuiverLauncher.Services;

namespace QuiverLauncher.ViewModels;

public sealed class MessagePromptViewModel : ObservableViewModel
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TaskCompletionSource<MessagePromptResult>? _completion;
    private string _title = "", _body = "";
    private bool _isOpen, _question, _includeCancel;
    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Body { get => _body; private set => Set(ref _body, value); }
    public bool IsOpen { get => _isOpen; private set => Set(ref _isOpen, value); }
    public bool IsQuestion { get => _question; private set => Set(ref _question, value); }
    public bool IncludeCancel { get => _includeCancel; private set => Set(ref _includeCancel, value); }
    public event Action<bool>? Opened;
    public async Task<MessagePromptResult> ShowAsync(string message, string title, bool question, bool preferCancelDefault, bool includeCancel, CancellationToken token)
    {
        try { await _gate.WaitAsync(token); }
        catch (OperationCanceledException) { return MessagePromptResult.Cancel; }
        TaskCompletionSource<MessagePromptResult>? completion = null;
        try
        {
            if (token.IsCancellationRequested) return MessagePromptResult.Cancel;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _completion = completion;
            Title = title; Body = message; IsQuestion = question; IncludeCancel = includeCancel && question;
            IsOpen = true;
            Opened?.Invoke(preferCancelDefault);
            return await completion.Task.WaitAsync(token);
        }
        catch (OperationCanceledException) { return MessagePromptResult.Cancel; }
        finally
        {
            if (completion != null && ReferenceEquals(_completion, completion)) { _completion = null; IsOpen = false; }
            _gate.Release();
        }
    }
    public void Complete(MessagePromptResult result)
    {
        if (_completion == null) return;
        IsOpen = false;
        _completion.TrySetResult(result);
    }
    public bool Dismiss()
    {
        if (!IsOpen) return false;
        Complete(IncludeCancel ? MessagePromptResult.Cancel : IsQuestion ? MessagePromptResult.No : MessagePromptResult.Yes);
        return true;
    }
}
