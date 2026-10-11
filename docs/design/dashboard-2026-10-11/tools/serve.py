# A static file server for the dashboard mockups, bound to 127.0.0.1 only.
# It writes its own pid to a file so that exactly this process can be stopped,
# sends no-store so an edited mockup shows on reload, and ends itself after
# eight hours so a forgotten server does not outlive the work.
import functools
import http.server
import os
import sys
import threading

root, port, pidfile = sys.argv[1], int(sys.argv[2]), sys.argv[3]

with open(pidfile, "w", encoding="ascii") as handle:
    handle.write(str(os.getpid()))


class Handler(http.server.SimpleHTTPRequestHandler):
    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def log_message(self, format, *args):
        pass


def stop_later():
    os._exit(0)


timer = threading.Timer(8 * 3600, stop_later)
timer.daemon = True
timer.start()

server = http.server.ThreadingHTTPServer(("127.0.0.1", port), functools.partial(Handler, directory=root))
server.serve_forever()
