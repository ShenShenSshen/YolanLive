using YolanLive.Core.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YolanLive.WinUI.Models
{
    public class PageArgs
    {
        public ILiveSite Site { get; set; }
        public object Data { get; set; }
        internal YolanLive.WinUI.Helper.RandomBrowseSession RandomSession { get; set; }
    }
}
