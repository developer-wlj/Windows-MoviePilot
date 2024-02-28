from os import path, getcwd

import click

from . import VERSION
from .api import Torrent
from .exceptions import RemoteUploadError, RemoteDownloadError
from .utils import humanize_filesize, upload_to_cache_server, get_open_trackers_from_remote, \
    get_open_trackers_from_local


@click.group()
@click.version_option(version='.'.join(map(str, VERSION)))
def start():
    """Torrentool command line utilities."""


@start.group()
def torrent():
    """Torrent-related commands."""


@torrent.command()
@click.argument('torrent_path', type=click.Path(exists=True, writable=False, dir_okay=False))
def info(torrent_path):
    """Print out information from .torrent file."""

    my_torrent = Torrent.from_file(torrent_path)

    size = my_torrent.total_size

    click.secho(f'Name: {my_torrent.name}', fg='blue')
    click.secho('Files:')
    for file_tuple in my_torrent.files:
        click.secho(file_tuple.name)

    click.secho(f'Hash: {my_torrent.info_hash}', fg='blue')
    click.secho(f'Size: {humanize_filesize(size)} ({size})', fg='blue')
    click.secho(f'Magnet: {my_torrent.get_magnet()}', fg='yellow')


@torrent.command()
@click.argument('source', type=click.Path(exists=True, writable=False))
@click.option('--dest', default=getcwd, type=click.Path(file_okay=False), help='Destination path to put .torrent file into. Default: current directory.')
@click.option('--tracker', default=None, help='Tracker announce URL (multiple comma-separated values supported).')
@click.option('--open_trackers', default=False, is_flag=True, help='Add open trackers announce URLs.')
@click.option('--comment', default=None, help='Arbitrary comment.')
@click.option('--cache', default=False, is_flag=True, help='Upload file to torrent cache services.')
def create(source, dest, tracker, open_trackers, comment, cache):
    """Create torrent file from a single file or a directory."""

    source_title = path.basename(source).replace('.', '_').replace(' ', '_')
    dest = f'{path.join(dest, source_title)}.torrent'

    click.secho(f'Creating torrent from {source} ...')

    my_torrent = Torrent.create_from(source)

    if comment:
        my_torrent.comment = comment

    urls = []

    if tracker:
        urls = tracker.split(',')

    if open_trackers:
        click.secho('Fetching an up-to-date open tracker list ...')
        try:
            urls.extend(get_open_trackers_from_remote())
        except RemoteDownloadError:
            click.secho('Failed. Using built-in open tracker list.', fg='red', err=True)
            urls.extend(get_open_trackers_from_local())

    if urls:
        my_torrent.announce_urls = urls

    my_torrent.to_file(dest)

    click.secho(f'Torrent file created: {dest}', fg='green')
    click.secho(f'Torrent info hash: {my_torrent.info_hash}', fg='blue')

    if cache:
        click.secho('Uploading to cache service ...')
        try:
            result = upload_to_cache_server(dest)
            click.secho(f'Cached torrent URL: {result}', fg='yellow')

        except RemoteUploadError as e:
            click.secho(f'Failed: {e}', fg='red', err=True)


def main():
    start(obj={})
