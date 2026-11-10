"""Create printable manuals with the Windows BIZ UD Gothic embedding permissions."""
from secrets import token_hex

from reportlab.lib.pdfencrypt import StandardEncryption
from reportlab.pdfgen.canvas import Canvas


class PrintPreviewEncryption(StandardEncryption):
    def permissionBits(self):
        # Also disable form filling and page assembly (PDF permission bits 9 and 11).
        return super().permissionBits() & ~0x0500


def manual_canvas(filename, pagesize):
    permissions = PrintPreviewEncryption(
        "", ownerPassword=token_hex(24), canPrint=1, canModify=0,
        canCopy=1, canAnnotate=0, strength=128,
    )
    return Canvas(str(filename), pagesize=pagesize, pdfVersion=(1, 4), encrypt=permissions)
