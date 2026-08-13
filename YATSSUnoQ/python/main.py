import atexit
import select
import socket
import threading

from arduino.app_utils import App, Bridge


TCP_PORT = 45991
command_buffer = bytearray()
client_socket = None
client_lock = threading.Lock()


def get_client():
    with client_lock:
        return client_socket


def replace_client(new_client=None):
    global client_socket
    with client_lock:
        old_client = client_socket
        client_socket = new_client
    if old_client is not None and old_client is not new_client:
        try:
            old_client.shutdown(socket.SHUT_RDWR)
        except OSError:
            pass
        try:
            old_client.close()
        except OSError:
            pass


def close_client(expected_client=None):
    global client_socket
    with client_lock:
        if expected_client is not None and client_socket is not expected_client:
            return
        old_client = client_socket
        client_socket = None
    if old_client is not None:
        try:
            old_client.close()
        except OSError:
            pass


server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
server_socket.bind(("0.0.0.0", TCP_PORT))
server_socket.listen(1)
server_socket.setblocking(False)


def clean_up():
    close_client()
    server_socket.close()


atexit.register(clean_up)


def on_yatss_frame(frame: str):
    line = frame.rstrip("\r\n") + "\n"
    encoded = line.encode("ascii", errors="replace")
    client = get_client()
    if client is not None:
        try:
            client.sendall(encoded)
        except OSError:
            close_client(client)


def relay_complete_commands(buffer: bytearray, data: bytes):
    buffer.extend(data)
    while b"\n" in buffer:
        raw_line, _, remainder = buffer.partition(b"\n")
        buffer.clear()
        buffer.extend(remainder)
        line = raw_line.rstrip(b"\r").decode("ascii", errors="replace")
        if line:
            try:
                Bridge.notify("yatss_command", line)
            except Exception as error:
                print(f"YATSS bridge command failed: {error}", flush=True)


def relay_commands():
    client = get_client()
    sources = [server_socket]
    if client is not None:
        sources.append(client)

    try:
        readable, _, _ = select.select(sources, [], [], 0.02)
    except (OSError, ValueError):
        close_client(client)
        return
    if not readable:
        return

    for source in readable:
        try:
            if source is server_socket:
                new_client, _ = server_socket.accept()
                new_client.setsockopt(socket.SOL_SOCKET, socket.SO_KEEPALIVE, 1)
                new_client.setblocking(False)
                replace_client(new_client)
                command_buffer.clear()
            elif source is client:
                data = client.recv(1024)
                if data:
                    relay_complete_commands(command_buffer, data)
                else:
                    close_client(client)
        except (OSError, ValueError) as error:
            print(f"YATSS transport operation failed: {error}", flush=True)
            if source is client:
                close_client(client)


Bridge.provide("yatss_frame", on_yatss_frame)
print(f"YATSS UNO Q controller available on TCP port {TCP_PORT}", flush=True)
App.run(user_loop=relay_commands)
