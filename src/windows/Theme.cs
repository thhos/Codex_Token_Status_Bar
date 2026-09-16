using System;
using System.Linq;
using System.Windows.Media;

namespace CodexPetCredits {
    public static class Palette {
        public static readonly string[] Keys = { "mint", "blue", "violet", "amber", "rose" };
        public static readonly string[] Names = { "薄荷", "海蓝", "鸢紫", "琥珀", "玫瑰" };
        private static readonly string[] DarkColors = { "#83D4B5", "#88BBF6", "#B7A4F6", "#EBC180", "#EEA2BB" };
        private static readonly string[] LightColors = { "#21785F", "#286AA8", "#7055AE", "#95651F", "#AB4869" };
        public static Color Accent(string key, bool dark) { int index = Math.Max(0,Array.IndexOf(Keys,key)); return (Color)ColorConverter.ConvertFromString((dark ? DarkColors : LightColors)[index]); }
        public static Color Series(string key, bool dark, int index) {
            if(index==0)return Accent(key,dark);
            var alternatives = Keys.Where(k=>k!=key).ToArray();
            return Accent(alternatives[(index-1)%alternatives.Length],dark);
        }
    }
    public static class CurveSmoothing {
        // Long-range bins already aggregate many hours; preserve more of their original variation.
        public static double Strength(TimeSpan window) {
            double hours=window.TotalHours;
            return hours<=1?1:hours<=6?.8:hours<=24?.55:hours<=168?.25:.1;
        }
        // Weighted five-bin smoothing affects the visual trend only; nulls break the filter's support.
        public static double?[] Apply(double?[] values) {
            return Apply(values,1);
        }
        public static double?[] Apply(double?[] values,double strength) {
            strength=Math.Max(0,Math.Min(1,strength));
            var result = new double?[values.Length]; int[] weights = {1,4,6,4,1};
            for(int i=0;i<values.Length;i++) {
                if(!values[i].HasValue)continue;
                double sum=values[i].Value*6, weight=6;
                foreach(int direction in new[]{-1,1}) for(int step=1;step<=2;step++) {
                    int at=i+direction*step;if(at<0 || at>=values.Length || !values[at].HasValue)break;
                    int w=weights[2+direction*step];sum+=values[at].Value*w;weight+=w;
                }
                result[i]=values[i].Value+(sum/weight-values[i].Value)*strength;
            } return result;
        }
    }
}
