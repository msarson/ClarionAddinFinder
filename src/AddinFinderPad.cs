using System.Windows.Forms;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Gui;

namespace AddinFinder
{
    public partial class AddinFinderPad : AbstractPadContent
    {
        private readonly Panel _contentPanel;

        public AddinFinderPad()
        {
            _contentPanel = new Panel { Dock = DockStyle.Fill };
            InitializeComponent();
        }

        public override System.Windows.Forms.Control Control => _contentPanel;
    }
}
