using System;
using System.Collections.Generic;
using System.Linq;

namespace CodexPetCredits {
    // Decode the wire format once. Controls work with named values, never nested JSON paths.
    internal sealed class UsageView {
        public readonly Dictionary<string,object> Settings;
        public readonly double? Remaining,ResetCredits,Total,RecentHour,RecentDay,OfficialCredits;
        public readonly string QuotaLabel,ForecastValue,ForecastLabel,TaskTitle,Subtitle,ChartKey,ResetLabel,Coverage,ForecastDetail,RateVersion;
        public readonly bool Warning;
        public readonly DateTime Start,End,ObservedAt;
        public readonly UsageSeries[] Series;
        public readonly UsageRow[] Models,OtherQuotas;
        public readonly ComparisonChoice[] Choices;
        public UsageView(object data){
            Settings=new Dictionary<string,object>(Json.Map(Json.Get(data,"settings")));
            Remaining=Number(data,"remaining");ResetCredits=Number(data,"resetCreditCount");Total=Number(data,"total");OfficialCredits=Number(data,"officialTaskCredits");
            RecentHour=Number(Json.Get(data,"recentCredits"),"hour");RecentDay=Number(Json.Get(data,"recentCredits"),"day");
            QuotaLabel=Json.Text(data,"quotaLabel").Contains("周")?"周额度剩余":"额度剩余";
            var prediction=Json.Get(data,"forecastDisplay");ForecastValue=Json.Text(prediction,"value","学习中");ForecastLabel=Json.Text(prediction,"label","耗尽预测");Warning=Json.Flag(data,"warning");
            TaskTitle=Json.Text(data,"taskTitle","暂无任务");Subtitle=Json.Text(data,"subtitle","本机已记录");
            ResetLabel=Json.Text(data,"resetLabel");Coverage=Json.Text(data,"coverage");ForecastDetail=Json.Text(data,"forecastDetail");RateVersion=Json.Text(data,"rateVersion");
            Start=Epoch(Json.Number(data,"windowStart"));End=Epoch(Json.Number(data,"windowEnd"));ObservedAt=Json.Get(data,"observedAt")==null?End:Epoch(Json.Number(data,"observedAt"));
            Series=Json.Items(Json.Get(data,"series")).Select(s=>new UsageSeries(Json.Text(s,"name"),Number(s,"total"),Json.Items(Json.Get(s,"points")).Select(NumberValue).ToArray())).ToArray();
            ChartKey=Json.Text(data,"chartKey",Json.Text(Settings,"scope")+":"+Json.Text(Settings,"range")+":"+String.Join("|",Series.Select(s=>s.Name)));
            Models=Rows(data,"details");OtherQuotas=Rows(data,"otherQuotas");
            Choices=Json.Items(Json.Get(data,"comparisonChoices")).Select(c=>new ComparisonChoice(Json.Text(c,"id"),Json.Text(c,"name"),Json.Flag(c,"selected"))).ToArray();
        }
        public static string Credits(double? value){return value.HasValue?value.Value.ToString("N1"):"—";}
        private static double? Number(object data,string key){return NumberValue(Json.Get(data,key));}
        private static double? NumberValue(object value){if(value==null)return null;double number=Convert.ToDouble(value);return Double.IsNaN(number)||Double.IsInfinity(number)?(double?)null:number;}
        private static DateTime Epoch(double milliseconds){return new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddMilliseconds(milliseconds).ToLocalTime();}
        private static UsageRow[] Rows(object data,string key){return Json.Items(Json.Get(data,key)).Select(r=>new UsageRow(Json.Text(r,"name"),Json.Text(r,"value"))).ToArray();}
    }
    internal sealed class UsageSeries {
        public readonly string Name;public readonly double? Total;public readonly double?[] Points;
        public UsageSeries(string name,double? total,double?[] points){Name=name;Total=total;Points=points;}
    }
    internal sealed class UsageRow {
        public readonly string Name,Value;
        public UsageRow(string name,string value){Name=name;Value=value;}
    }
    internal sealed class ComparisonChoice {
        public readonly string Id,Name;public readonly bool Selected;
        public ComparisonChoice(string id,string name,bool selected){Id=id;Name=name;Selected=selected;}
    }
}
