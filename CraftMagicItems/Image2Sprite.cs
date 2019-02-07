using System.IO;
using UnityEngine;

namespace CraftMagicItems {
    // Loosely based on https://forum.unity.com/threads/generating-sprites-dynamically-from-png-or-jpeg-files-in-c.343735/
    static class Image2Sprite {
        public static Sprite Create(string filePath) {
            var bytes = File.ReadAllBytes(filePath);
            var texture = new Texture2D(64, 64);
            texture.LoadImage(bytes);
            return Sprite.Create(texture, new Rect(0, 0, 64, 64), new Vector2(0, 0));
        }
    }
}