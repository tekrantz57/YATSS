import atexit
import os
import pty
import select
import socket
import tty

from arduino.app_utils import App, Bridge


# App Lab runs this process in a container. /app is the app directory bind
# mounted from the UNO Q host, so this link is visible to Wine outside it.
LINK_PATH = "/app/yatss-unoq"
TCP_PORT = 45991
master_fd, slave_fd = pty.openpty()
tty.setraw(slave_fd)
slave_path = os.ttyname(slave_fd)
pty_command_buffer = bytearray()
tcp_command_buffer = bytearray()
client_socket = None


def close_client():
    global client_socket
    if client_socket is not None:
        try:
            client_socket.close()
        except OSError:
            pass
        client_socket = None


def remove_link():
    try:
        if os.path.islink(LINK_PATH):
            os.unlink(LINK_PATH)
    except OSError:
        pass


remove_link()
server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
server_socket.bind(("0.0.0.0", TCP_PORT))
server_socket.listen(1)
server_socket.setblocking(False)
os.symlink(slave_path, LINK_PATH)


def clean_up():
    close_client()
    server_socket.close()
    remove_link()


atexit.register(clean_up)


def on_yatss_frame(frame: str):
    line = frame.rstrip("\r\n") + "\n"
    encoded = line.encode("ascii", errors="replace")
    os.write(master_fd, encoded)
    if client_socket is not None:
        try:
            client_socket.sendall(encoded)
        except OSError:
            close_client()


def relay_complete_commands(buffer: bytearray, data: bytes):
    buffer.extend(data)
    while b"\n" in buffer:
        raw_line, _, remainder = buffer.partition(b"\n")
        buffer.clear()
        buffer.extend(remainder)
        line = raw_line.rstrip(b"\r").decode("ascii", errors="replace")
        if line:
            Bridge.notify("yatss_command", line)


def relay_commands():
    global client_socket
    sources = [master_fd, server_socket]
    if client_socket is not None:
        sources.append(client_socket)

    readable, _, _ = select.select(sources, [], [], 0.02)
    if not readable:
        return

    for source in readable:
        if source is server_socket:
            new_client, _ = server_socket.accept()
            new_client.setblocking(False)
            close_client()
            client_socket = new_client
            tcp_command_buffer.clear()
        elif source == master_fd:
            data = os.read(master_fd, 1024)
            if data:
                relay_complete_commands(pty_command_buffer, data)
        elif source is client_socket:
            try:
                data = client_socket.recv(1024)
            except OSError:
                data = b""
            if data:
                relay_complete_commands(tcp_command_buffer, data)
            else:
                close_client()


Bridge.provide("yatss_frame", on_yatss_frame)
print(
    f"YATSS UNO Q controller available on TCP port {TCP_PORT} "
    f"and at {LINK_PATH} -> {slave_path}",
    flush=True,
)
App.run(user_loop=relay_commands)
