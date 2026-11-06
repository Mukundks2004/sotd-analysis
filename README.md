# SOTD Analysis

## Getting Started

**Prerequisites**:

- .NET 10
- yt-dlp
- spotify premium account
- discord bot

1. Fill in `config.json` based off `config.example.json`.

2. Run:

```cmd
dotnet run --project Importer
```

### Admin UI

To browse and edit the database, run the API and the web app in two terminals:

```cmd
dotnet run --project Backend
```

```cmd
cd Frontend && npm install && npm run dev
```

Then open http://localhost:5173. The dev server proxies `/api` and `/files` to the
backend on http://localhost:5280.

## About

This repo has all the code required to do a sotd analysis

This includes the following solutions. Each of them is in a dedicated folder with a spec `.md` file.

1. Relational database schema for storing messages, days, songs, authors, etc., a human readable description of the schema
1. A WebAPI with a browser-based UI that runs in localhost for performing CRUD operations on the DB
1. A queries folder that contains a running list of ideas for things to query and analyse, and results of those queries
1. Code for an importer that can run on demand and will enter data into the database

## What is SOTD?

Sotd stands for song of the day. It is a daily ritual amongst my friend group in a shared discord server in a specific set of "sotd" channels. (Almost) every day for the last 5 years, one or more songs are posted in the sotd channel as sotd entries- these are all "songs of the day".

A SOTD entry can be in the form of:

- a youtube video link
- a spotify or apple music song link
- a raw music or video file upload
- a link to a vimeo (or some other video hosting site) video
- any other way to upload audio media
- sometimes multiple links to songs are sent in one discord message, each link is its own entry
- sometimes a youtube or spotify playlist is linked. Each song in the playlist is one entry

What is not an entry:

- A message with a song name without a link to a song
- An image, or a link to an image, or some other kind of media without audio

## What is the SOTD Analysis Project?

A project to catalogue and identify trends on all SOTD entries. At the core is an importer that can be run on a channel and will read all messages and enter them into a database so that queries can be run. Some other supporting infrastructure is in this repo too.
