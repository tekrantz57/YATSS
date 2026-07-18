using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace tlp
{
    public  class Lane
    {
        int lane;
        List<int> times;
        int carriedLapCount;
        int manualLapAdjustment;
        public int best_time { get; set; }
              
        public Lane(int l)
        {
            lane = l;
            best_time = Int32.MaxValue;
            times = new List<int>();
        }

        public void AddLap(int millis, bool eligibleForBest = true)
        {
            times.Add(millis);
            if (eligibleForBest && millis < best_time)
                best_time = millis;
        }

        public void AddLapCountOnly()
        {
            carriedLapCount++;
        }

        public int AdjustLapCount(int delta)
        {
            int baseCount = carriedLapCount + times.Count;
            manualLapAdjustment = Math.Max(-baseCount, manualLapAdjustment + delta);
            return getCount();
        }

        public void ResetTiming(int lapCount)
        {
            carriedLapCount = lapCount;
            manualLapAdjustment = 0;
            times.Clear();
            best_time = Int32.MaxValue;
        }

        public int getCount()
        {
            return carriedLapCount + times.Count() + manualLapAdjustment;
        }

        public int getMedian()
        {
            int[] temp = times.ToArray();
            Array.Sort(temp);

            int count = temp.Length;
            if (count == 0)
            {
                return 0;
            }
            else if (count % 2 == 0)
            {
                // count is even, average two middle elements
                int a = temp[count / 2 - 1];
                int b = temp[count / 2];
                return (a + b) / 2;
            }
            else
            {
                // count is odd, return the middle element
                return temp[count / 2];
            }
        }
    }
}
