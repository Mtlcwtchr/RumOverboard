#if FUSION2
namespace RumOverboard.Gameplay.Interaction
{
    /// <summary>
    /// One-shot interactable: Interact runs an action on the host (no attached state, no station),
    /// e.g. casting off / making fast the mooring lines at a pier bollard. Called by
    /// <see cref="RumOverboard.Networking.NetworkPlayer"/> after the target passed the host range +
    /// <see cref="Interactable.IsAvailable"/> checks, only while the crew member's hands are free.
    /// </summary>
    public interface IHostUsable
    {
        void HostUse(in InteractorInfo who);
    }
}
#endif
