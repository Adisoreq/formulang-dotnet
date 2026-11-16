using Microsoft.AspNetCore.Components;
using WebPreview.Model;

namespace WebPreview.Components
{
    /// <summary>A component that renders the state of the preview: it renders again after every change of the state.</summary>
    public abstract class StateComponent : ComponentBase, IDisposable
    {
        [Inject]
        protected PreviewState State { get; set; } = default!;

        protected override void OnInitialized() => State.Changed += OnChanged;

        private void OnChanged()
        {
            OnStateChanged();
            _ = InvokeAsync(StateHasChanged);
        }

        /// <summary>Called after every change of the state, before the component renders again.</summary>
        protected virtual void OnStateChanged() { }

        public virtual void Dispose() => State.Changed -= OnChanged;
    }
}
