using UnityEngine;

namespace Showcase.Experiences.Station
{
    public enum StationPropKind { Radio, Locker, Spanner, AirConditioner, Switch, Airlock, Monitor, Decoy, Keypad }

    // Tappable object in the station room; the experience decides what a tap means.
    public class StationProp : MonoBehaviour, ITappable
    {
        public StationPropKind Kind;
        public int Index;
        [TextArea] public string Message;

        public void OnTap(RaycastHit hit)
        {
            var station = GetComponentInParent<StationExperience>();
            if (station != null)
                station.HandleTap(this);
        }
    }
}
