using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Vortice.Mathematics;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using Point = System.Drawing.Point;

namespace TextEditingInPreviewPlugin.Services
{
    public static class CoordinateConverter
    {
        private static readonly BindingFlags AllFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static (IVideoItem? item, Vector2[]? quad, DrawDescription? desc) FindItemAtPosition(object playerInstance, Vector2 mousePos)
        {
            try
            {
                var type = playerInstance.GetType();
                var timelineVideoField = type.GetField("timelineVideo", AllFlags);
                if (timelineVideoField == null) return (null, null, null);

                var timelineVideo = timelineVideoField.GetValue(playerInstance);
                if (timelineVideo == null) return (null, null, null);

                var timelineItemRectsProp = timelineVideo.GetType().GetProperty("TimelineItemRects", AllFlags)
                                         ?? timelineVideo.GetType().GetProperty("TimelineItemRects");
                if (timelineItemRectsProp == null) return (null, null, null);

                var rects = timelineItemRectsProp.GetValue(timelineVideo) as IEnumerable;
                if (rects == null) return (null, null, null);

                lock (rects)
                {
                    foreach (var tuple in rects)
                    {
                        var tupleType = tuple.GetType();
                        var itemField = tupleType.GetField("Item1") ?? tupleType.GetField("item");
                        var quadField = tupleType.GetField("Item3") ?? tupleType.GetField("quad");
                        var descField = tupleType.GetField("Item4") ?? tupleType.GetField("desc");

                        var item = itemField?.GetValue(tuple) as IVideoItem;
                        var quad = quadField?.GetValue(tuple) as Vector2[];
                        var desc = descField?.GetValue(tuple) as DrawDescription;

                        if (item != null && !item.IsLocked && quad != null)
                        {
                            if (IsPointInQuad(quad, mousePos))
                            {
                                return (item, quad, desc);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FindItemAtPosition Error] {ex}");
            }

            return (null, null, null);
        }

        public static bool IsPointInQuad(Vector2[] quad, Vector2 point)
        {
            if (quad == null || quad.Length < 4) return false;

            int crossings = 0;
            int count = quad.Length;

            for (int i = 0; i < count; i++)
            {
                Vector2 a = quad[i];
                Vector2 b = quad[(i + 1) % count];

                if ((a.Y <= point.Y && point.Y < b.Y) || (b.Y <= point.Y && point.Y < a.Y))
                {
                    float t = (point.Y - a.Y) / (b.Y - a.Y);
                    float x = a.X + t * (b.X - a.X);
                    if (point.X < x)
                    {
                        crossings++;
                    }
                }
            }

            return (crossings % 2) != 0;
        }

        public static Vector2 VideoToClientPoint(object playerInstance, Vector2 videoPos)
        {
            var type = playerInstance.GetType();

            float previewAreaZoom = (float)(type.GetProperty("PreviewAreaZoom", AllFlags)?.GetValue(playerInstance) ?? 1f);
            float displayZoom = (float)(type.GetProperty("PreviewDisplayZoom", AllFlags)?.GetValue(playerInstance) ?? 1f);
            Vector2 viewCenter = (Vector2)(type.GetProperty("PreviewViewCenter", AllFlags)?.GetValue(playerInstance) ?? Vector2.Zero);
            float hostWidth = (float)(type.GetField("hostWidth", AllFlags)?.GetValue(playerInstance) ?? 0f);
            float hostHeight = (float)(type.GetField("hostHeight", AllFlags)?.GetValue(playerInstance) ?? 0f);

            // ビデオ座標 -> フィット座標: fitPos = (videoPos - viewCenter) * displayZoom
            Vector2 fitPos = (videoPos - viewCenter) * displayZoom;

            // フィット座標 -> クライアントピクセル座標:
            float cx = (fitPos.X * previewAreaZoom + hostWidth / 2f);
            float cy = (fitPos.Y * previewAreaZoom + hostHeight / 2f);

            return new Vector2(cx, cy);
        }

        public static System.Windows.Point ClientToScreenPoint(object playerInstance, Vector2 clientPoint)
        {
            var type = playerInstance.GetType();
            var host = type.GetField("host", AllFlags)?.GetValue(playerInstance) as WindowsFormsHost;
            var child = host?.Child;
            if (child != null && child.IsHandleCreated)
            {
                Point p = child.PointToScreen(new Point((int)clientPoint.X, (int)clientPoint.Y));
                return new System.Windows.Point(p.X, p.Y);
            }
            return new System.Windows.Point(clientPoint.X, clientPoint.Y);
        }

        public static float GetPreviewScreenZoom(object playerInstance)
        {
            var type = playerInstance.GetType();
            float previewAreaZoom = (float)(type.GetProperty("PreviewAreaZoom", AllFlags)?.GetValue(playerInstance) ?? 1f);
            float displayZoom = (float)(type.GetProperty("PreviewDisplayZoom", AllFlags)?.GetValue(playerInstance) ?? 1f);
            return previewAreaZoom * displayZoom;
        }

        public static WindowsFormsHost? GetHost(object playerInstance)
        {
            var type = playerInstance.GetType();
            return type.GetField("host", AllFlags)?.GetValue(playerInstance) as WindowsFormsHost;
        }

        public static Timeline? GetTimeline(object playerInstance)
        {
            var type = playerInstance.GetType();
            return (type.GetField("timeline", AllFlags) ?? type.BaseType?.GetField("timeline", AllFlags))?.GetValue(playerInstance) as Timeline;
        }

        public static void NotifyTimelineChanged(object playerInstance)
        {
            try
            {
                var type = playerInstance.GetType();
                var field = type.GetField("isTimelineChanged", AllFlags);
                field?.SetValue(playerInstance, true);
            }
            catch { }
        }
    }
}
