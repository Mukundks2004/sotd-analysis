# Frontend

This is a React + Typescript frontend (using material ui component library) that provides users a way to manually make changes in the database without having to run SQL. There are three features: CRUD UI, many-to-many annotation, and genre visualisation. Each feature should be a 'tab' that you can select to use that feature.

## CRUD UI

The update and delete methods have a confirmation dialog. Create has a dedicated form. There are 3 buttons in the top right called 'create', 'update', and 'delete' whose functions are described below.

### Read

Each DB table that is not a join entity presents as a physical table. Data table or similar should be used for this, but a custom component may be needed if there is no component that meets all the requirements listed here.

You can select which table to edit with a dropdown at the top of the page. The tables marked as "big" use endpoints to paginate rows, the tables marked as "small" load all data.

The format of the page visually is dropdown at the top of the page and table name below it on the right. The rest is 'inside' the table. At the top of the table, on the left a dropdown to change pagination number for the tables that support it- either 10, 20, or 50 results per page. On the right a filter search bar, a square button with an excel logo, and a square button with a printer logo.

- The filter bar should filter any string column that matches the filter text. It should not include the binary 'profile picture' column.
- The excel logo downloads what the current table looks like as a csv
- The printer logo opens a print dialog, to print or turn it into a pdf

Beneath this are the columns. For the columns in each table, the default ordering is using the default column specified in `Database/spec.md`. If the user clicks the name of a column in the table, a little up arrow should appear to show that we are ordering by that column and the table should refresh. If that column is clicked again, the up arrow should swap to down to show it is ordered descending.

Beneath that is the data. At the bottom of the table is the 'next page/previous page' button.

For the profile table, there is a "profile picture" binary column. Please turn this into an image and display it in the column rather than displaying the raw binary text.

### Update

Primary keys are not editable, they are greyed out. Other editable columns can be clicked for any row to edit that row. To confirm a change, you have to click the 'update' button and a confirmation dialog will display.

For foreign keys and columns with constraints, you can edit them (the guid or whatever the column type is) but if it fails a constraint, e.g., points to a row that does not exist in another table, the backend will return an error. The table does not display success or any change optimistically, it wait for a response and then displays the success in terms of a toast that pops up (green or red depending) and only then makes the change.

### Create

The 'create' button opens a dedicated form modal with fields for all controllable columns. Same as update, if you specify a foreign key that fails the foreign key constraint, then the create request fails. The form does not close unless the request was successful. If it fails it shows the error message, stays open, and keeps all the data to let the user correct the error. The form should remember what the partially completed details are if the form is exited via the close button or by clicking out of it.

### Delete

For each table the leftmost column there should be a checkbox column to select rows. The user can select multiple rows, and the (previously greyed out) delete button becomes enabled as soon as a row is selected. Clicking delete opens a confirmation modal. Same thing, it does not actually remove the data unless the delete operation succeeds.

## Many-to-Many Annotations

For some relationships such as songs and genres, there is a many to many relationship in the form of a join table. The following system is used to edit these relationships.

Not all join entities can be edited. The only ones that can be edited are the following:

- Fixing song and editing artists
- Fixing song and editing genre
- Fixing profile and editing person

First select which relationship to edit in the dropdown, e.g. 'Song to Genre'.

For each song (remember to paginate if big), there is a second column showing in a 'tag' system all the second entity, in this case genres, associated with that song. If you hover over a tag, a little 'x' shows up on the right of the name of the tag allowing you to delete it.

Realistically there will not be more than 10 tags ever.

For each row there is a search bar at the end to search for more tags. By default, when you click on the search bar in a particular row, it loads all tags and displays them in a dropdown under the search bar. If there are no results, then instead of displaying _no results_ in this dropdown, it displays _add X_ where _X_ is what you have typed in. Selecting this generates the artist/genre/person, inserts it into the db, inserts this row in the join entity, and displays the tag in the FE.

Note that for both artist and person there are no columns other than name, so this tagging system would fill the entire row. For genre, which has another column that is an FK to genre, this tagging system cannot populate the whole row. But this column is nullable so it is ok to leave it null.

This system allows for rapidly annotating many-to-many relationships.

## Genre Visualisation

The third function is to visualise genre hierarchy via a force directed graph drawing system. All genres are nodes, floating around. You can draw a directed arrow from one genre to another. This makes the first node the parent and the second node the child.

You can use a library like D3 for tidy tree drawing if it is easier, provided this supports dynamic node movement not static images. You should be able to left click to drag nodes around. The root nodes should feel a force to move to the top of the page and the children should come beneath them- below the parent but above the children. Plus the other force directed graph drawing forces- move towards the middle, repel from another nodes, move closer where there is an edge between two nodes.

Right clicking opens a text box where if you type a name and click create, it creates a new genre and enters it in the db and shows the node in the visualisation. Clicking out, or hitting escape, closes this box.

I don't ever expect there to be more than 50 or so genres, so the text/nodes can be reasonably big.

Additionally, there will be several trees since the 'pop' genre hierarchy is separate to the 'rock' one.
