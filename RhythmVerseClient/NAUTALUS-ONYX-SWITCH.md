

onyx import $FILE
creates
./$FILE_import/
append
  ps:
    game: ps
to
./$FILE_import/song.yml
onyx build ./$FILE_import/song.yml --target ps --to /out/put/dir
creates the clone hero dir