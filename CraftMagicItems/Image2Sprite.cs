using System.IO;
using UnityEngine;

namespace CraftMagicItems {
    // Loosely based on https://forum.unity.com/threads/generating-sprites-dynamically-from-png-or-jpeg-files-in-c.343735/
    static class Image2Sprite {
        public static Sprite Create(string filePath) {
            byte[] bytes = File.ReadAllBytes(filePath);
            Texture2D texture = new Texture2D(1, 1);
            ImageConversion.LoadImage(texture, bytes);
            return Sprite.Create(texture, new Rect(0, 0, 64, 64), new Vector2(0, 0));
        }
    }
}