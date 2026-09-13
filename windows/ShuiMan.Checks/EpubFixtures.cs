using System.Text;

namespace ShuiMan.Checks;

internal static class EpubFixtures
{
    internal static void Generate(string directory, byte[] first, byte[] second)
    {
        byte[] Text(string value) => Encoding.UTF8.GetBytes(value);
        Fixtures.Archive(System.IO.Path.Combine(directory, "native-images.epub"), new()
        {
            ["META-INF/container.xml"] = Text("<container xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile full-path=\"OPS/book.opf\"/></rootfiles></container>"),
            ["OPS/book.opf"] = Text("""
                <package xmlns="http://www.idpf.org/2007/opf" version="3.0"><manifest>
                <item id="multi" href="multi.xhtml" media-type="application/xhtml+xml"/>
                <item id="svg" href="vector.xhtml" media-type="application/xhtml+xml"/>
                <item id="background" href="background.xhtml" media-type="application/xhtml+xml"/>
                <item id="text" href="text.xhtml" media-type="application/xhtml+xml"/>
                <item id="rotate" href="rotate.xhtml" media-type="application/xhtml+xml"/>
                <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
                </manifest><spine page-progression-direction="rtl"><itemref idref="multi"/><itemref idref="svg"/>
                <itemref idref="background"/><itemref idref="text"/><itemref idref="rotate"/><itemref idref="multi"/><itemref idref="missing"/></spine></package>
                """),
            ["OPS/multi.xhtml"] = Text("<html><head><link rel='stylesheet' href='styles/book.css'/></head><body><p>Original generated text accompanies the comic.</p><img src='images/second.png'/><img src='images/missing.png'/><img src='images/first.png'/></body></html>"),
            ["OPS/vector.xhtml"] = Text("<html><body><svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' viewBox='0 0 480 720'><image width='480' height='720' xlink:href='images/first.png'/></svg></body></html>"),
            ["OPS/background.xhtml"] = Text("<html><head><link rel='stylesheet' href='styles/book.css'/></head><body><div class='page'></div><div style=\"background: url('images/first.png')\"></div></body></html>"),
            ["OPS/text.xhtml"] = Text("<html><body><p>An original text-only reading position.</p></body></html>"),
            ["OPS/rotate.xhtml"] = Text("<html><head><link rel='stylesheet' href='styles/book.css'/></head><body><div class='rotated'><img src='images/second.png'/></div></body></html>"),
            ["OPS/styles/book.css"] = Text(".unused { transform:rotate(180deg); } .page { position:absolute; background-image:url('../images/second.png'); } .rotated { transform:rotate(90deg); }"),
            ["OPS/nav.xhtml"] = Text("<html xmlns:epub='http://www.idpf.org/2007/ops'><body><nav epub:type='toc'><a href='vector.xhtml'>Original image wrapper</a><a href='rotate.xhtml'>Original rotated page</a></nav></body></html>"),
            ["OPS/images/first.png"] = first,
            ["OPS/images/second.png"] = second
        });
        Fixtures.Archive(System.IO.Path.Combine(directory, "rotation-cascade.epub"), new()
        {
            ["META-INF/container.xml"] = Text("<container xmlns='urn:oasis:names:tc:opendocument:xmlns:container'><rootfiles><rootfile full-path='book.opf'/></rootfiles></container>"),
            ["book.opf"] = Text("<package xmlns='http://www.idpf.org/2007/opf' version='3.0'><manifest><item id='page' href='page.xhtml' media-type='application/xhtml+xml'/></manifest><spine><itemref idref='page'/></spine></package>"),
            ["page.xhtml"] = Text("""
                <html><head><style>.unused {transform:rotate(180deg)} .turned {transform:rotate(90deg)}
                img.turned {transform:rotate(180deg)} .explicit-zero {transform:rotate(0deg)}
                .important {transform:rotate(270deg)!important}</style></head><body>
                <img src='image.png'/><img class='turned' src='image.png'/>
                <img class='turned' style='transform:rotate(90deg)' src='image.png'/>
                <div style='transform:rotate(90deg)'><img style='transform:rotate(270deg)' src='image.png'/></div>
                <img class='explicit-zero' src='image.png'/><img class='important' style='transform:rotate(90deg)' src='image.png'/>
                <img style='transform:rotate(45deg)' src='image.png'/>
                <svg xmlns='http://www.w3.org/2000/svg'><image href='image.png' transform='rotate(90 100 100)'/>
                <image href='image.png' transform='rotate(90)' style='transform:none'/></svg>
                </body></html>
                """),
            ["image.png"] = first
        });
    }
}
