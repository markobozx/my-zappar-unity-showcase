using UnityEngine;

namespace Showcase
{
    // One choice in the ShowcaseTray. Give it a Model (shown as a rendered thumbnail and in the live
    // preview), an Icon texture, or a Swatch colour (for colour/material choices).
    public class TrayOption
    {
        public string Label;            // tile caption and the big name
        public string Price;            // accent line under the name, e.g. "€649" or "+12 pts"
        public string Detail;           // muted description under that
        public GameObject Model;
        public Texture Icon;
        public Color Swatch = Color.clear;
        public string Badge;            // small corner badge on the tile, e.g. "♥" or "√"

        public TrayOption(string label, string price = null, string detail = null, GameObject model = null)
        {
            Label = label;
            Price = price;
            Detail = detail;
            Model = model;
        }
    }
}
