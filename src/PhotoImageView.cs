using System;

namespace FSpot {
	public class PhotoImageView : ImageView {
		public delegate void PhotoChangedHandler (PhotoImageView view);
		public event PhotoChangedHandler PhotoChanged;
		
		protected BrowsablePointer item;
		protected FSpot.Loupe loupe;
		protected FSpot.Loupe sharpener;

		public PhotoImageView (IBrowsableCollection query)
		{
			loader = new FSpot.AsyncPixbufLoader ();
			loader.AreaUpdated += HandlePixbufAreaUpdated;
			loader.AreaPrepared += HandlePixbufPrepared;
			loader.Done += HandleDone;

			Accelerometer.OrientationChanged += HandleOrientationChanged;

			this.SizeAllocated += HandleSizeAllocated;
			this.KeyPressEvent += HandleKeyPressEvent;
			this.ScrollEvent += HandleScrollEvent;
			this.item = new BrowsablePointer (query, -1);
			item.Changed += PhotoItemChanged;
			this.Destroyed += HandleDestroyed;
			this.SetTransparentColor (this.Style.BaseColors [(int)Gtk.StateType.Normal]);
		}
		
		protected override void OnStyleSet (Gtk.Style previous)
		{
			this.SetTransparentColor (this.Style.Backgrounds [(int)Gtk.StateType.Normal]);
		}

		new public BrowsablePointer Item {
			get {
				return item;
			}
		}

		private IBrowsableCollection query;
		public IBrowsableCollection Query {
			get {
				return item.Collection;
			}
#if false
			set {
				if (query != null) {
					query.Changed -= HandleQueryChanged;
					query.ItemsChanged -= HandleQueryItemsChanged;
				}

				query = value;
				query.Changed += HandleQueryChanged;
				query.ItemsChanged += HandleQueryItemsChanged;
			}
#endif
		}

		public Gdk.Pixbuf CompletePixbuf ()
		{
			loader.LoadToDone ();
			return this.Pixbuf;
		}

		public void HandleOrientationChanged (object sender)
		{
			Reload ();
		}

		public void Reload ()
		{
			if (!Item.IsValid)
				return;
			
			PhotoItemChanged (Item, null);
		}

		// Display.
		private void HandlePixbufAreaUpdated (object sender, AreaUpdatedArgs args)
		{
			Gdk.Rectangle area = this.ImageCoordsToWindow (args.Area);
			this.QueueDrawArea (area.X, area.Y, area.Width, area.Height);
		}
		
		private void HandlePixbufPrepared (object sender, AreaPreparedArgs args)
		{
			Gdk.Pixbuf prev = this.Pixbuf;
			Gdk.Pixbuf next = loader.Pixbuf;

			this.Pixbuf = next;
			if (prev != null)
				prev.Dispose ();

			UpdateMinZoom ();
			this.ZoomFit (args.ReducedResolution);
		}

		private void HandleDone (object sender, System.EventArgs args)
		{
			// FIXME the error hander here needs to provide proper information and we should
			// pass the state and the write exception in the args
			Gdk.Pixbuf prev = this.Pixbuf;
			if (loader.Pixbuf == null) {
				System.Exception ex = null;

				// FIXME in some cases the image passes completely through the
				// pixbuf loader without properly loading... I'm not sure what to do about this other
				// than try to load the image one last time.
				this.Pixbuf = null;
				if (!loader.Loading) {
					try {
						System.Console.WriteLine ("Falling back to file loader");

						this.Pixbuf = FSpot.PhotoLoader.Load (item.Collection, 
										      item.Index);
					} catch (System.Exception e) {
						if (!(e is GLib.GException))
							System.Console.WriteLine (e.ToString ());

						ex = e;
					}
				}

				if (this.Pixbuf == null) {
					LoadErrorImage (ex);
				} else {
					UpdateMinZoom ();
					this.ZoomFit ();
				}
			} else {
				this.Pixbuf = loader.Pixbuf;

				if (!loader.Prepared) {
					UpdateMinZoom ();
					this.ZoomFit ();
				}
			}

			if (prev != this.Pixbuf && prev != null)
				prev.Dispose ();
		}
		
		private bool fit = true;
		public bool Fit {
			get {
				return (Zoom == MIN_ZOOM);
			}
			set {
				if (!fit && value)
					ZoomFit ();
				
				fit = value;
			}
		}


		public double Zoom {
			get {
				double x, y;
				this.GetZoom (out x, out y);
				return x;
			}
			
			set {
				//Console.WriteLine ("Setting zoom to {0}, MIN = {1}", value, MIN_ZOOM);
				value = System.Math.Min (value, MAX_ZOOM);
				value = System.Math.Max (value, MIN_ZOOM);

				double zoom = Zoom;
				if (value == zoom)
					return;

				if (System.Math.Abs (zoom - value) < System.Double.Epsilon)
					return;

				if (value == MIN_ZOOM)
					this.Fit = true;
				else {
					this.Fit = false;
					this.SetZoom (value, value);
				}
			}
		}
		
		// Zoom scaled between 0.0 and 1.0
		public double NormalizedZoom {
			get {
				return (Zoom - MIN_ZOOM) / (MAX_ZOOM - MIN_ZOOM);
			}
			set {
				Zoom = (value * (MAX_ZOOM - MIN_ZOOM)) + MIN_ZOOM;
			}
		}
		
		private void HandleSizeAllocated (object sender, Gtk.SizeAllocatedArgs args)
		{
			if (fit)
				ZoomFit ();
		}

		bool load_async = true;
		FSpot.AsyncPixbufLoader loader;

		private void LoadErrorImage (System.Exception e)
		{
			// FIXME we should check the exception type and do something
			// like offer the user a chance to locate the moved file and
			// update the db entry, but for now just set the error pixbuf
			
			Gdk.Pixbuf old = this.Pixbuf;
			this.Pixbuf = new Gdk.Pixbuf (PixbufUtils.ErrorPixbuf, 0, 0, 
						      PixbufUtils.ErrorPixbuf.Width, 
						      PixbufUtils.ErrorPixbuf.Height);
			if (old != null)
				old.Dispose ();
			
			UpdateMinZoom ();
			this.ZoomFit ();
		}

		private void PhotoItemChanged (BrowsablePointer item, BrowsablePointerChangedArgs args) 
		{
			System.Console.WriteLine ("item changed");
			// If it is just the position that changed fall out
			if (args != null && 
			    args.PreviousItem != null &&
			    Item.IsValid &&
			    (args.PreviousIndex != item.Index) &&
			    (this.Item.Current.DefaultVersionUri == args.PreviousItem.DefaultVersionUri))
				return;

			if (load_async) {
				Gdk.Pixbuf old = this.Pixbuf;
				try {
					if (Item.IsValid) {
						System.Uri uri = Item.Current.DefaultVersionUri;
						loader.Load (uri);
					} else
						LoadErrorImage (null);

				} catch (System.Exception e) {
					System.Console.WriteLine (e.ToString ());
					LoadErrorImage (e);
				}
				if (old != null)
					old.Dispose ();
			} else {	
				Gdk.Pixbuf old = this.Pixbuf;
				this.Pixbuf = FSpot.PhotoLoader.Load (item.Collection, 
								      item.Index);
				if (old != null)
					old.Dispose ();

				UpdateMinZoom ();
				this.ZoomFit ();
			}
			
			this.UnsetSelection ();

			if (PhotoChanged != null)
				PhotoChanged (this);
		}
		
		public void ZoomIn ()
		{
			Zoom = Zoom * ZOOM_FACTOR;
		}
		
		public void ZoomOut ()
		{
			Zoom = Zoom / ZOOM_FACTOR;
		}
		
		bool upscale;
		private void ZoomFit ()
		{
			ZoomFit (upscale);
		}

		private void ZoomFit (bool upscale)
		{			
			Gdk.Pixbuf pixbuf = this.Pixbuf;
			Gtk.ScrolledWindow scrolled = this.Parent as Gtk.ScrolledWindow;
			this.upscale = upscale;
			
			if (pixbuf == null)
				return;

			int available_width = this.Allocation.Width;
			int available_height = this.Allocation.Height;
		
			double zoom_to_fit = ZoomUtils.FitToScale ((uint) available_width, 
								   (uint) available_height,
								   (uint) pixbuf.Width, 
								   (uint) pixbuf.Height, 
								   upscale);
			
			double image_zoom = zoom_to_fit;
			/*
			System.Console.WriteLine ("Zoom = {0}, {1}, {2}", image_zoom, 
						  available_width, 
						  available_height);
			*/

			if (scrolled != null)
				scrolled.SetPolicy (Gtk.PolicyType.Never, Gtk.PolicyType.Never);

			this.SetZoom (image_zoom, image_zoom);
			
			if (scrolled != null)
				scrolled.SetPolicy (Gtk.PolicyType.Automatic, Gtk.PolicyType.Automatic);
		}
		
		private void HandleLoupeDestroy (object sender, EventArgs args)
		{
			if (sender == loupe)
				loupe = null;

			if (sender == sharpener)
				sharpener = null;

		}

		[GLib.ConnectBefore]
		private void HandleKeyPressEvent (object sender, Gtk.KeyPressEventArgs args)
		{
			// FIXME I really need to figure out why overriding is not working
			// for any of the default handlers.
			args.RetVal = true;
		
			// Check for KeyPad arrow keys, which scroll the window when zoomed in
			// but should go to the next/previous photo when not zoomed (no scrollbars)
			if (this.Fit) {
				switch (args.Event.Key) {
				case Gdk.Key.Up:
				case Gdk.Key.Left:
				case Gdk.Key.KP_Up:
				case Gdk.Key.KP_Left:
					this.Item.MovePrevious ();
					return;
				case Gdk.Key.Down:
				case Gdk.Key.Right:
				case Gdk.Key.KP_Down:
				case Gdk.Key.KP_Right:
					this.Item.MoveNext ();
					return;
				}
			}

			switch (args.Event.Key) {
			case Gdk.Key.Page_Up:
			case Gdk.Key.KP_Page_Up:
				this.Item.MovePrevious ();
				break;
			case Gdk.Key.Home:
			case Gdk.Key.KP_Home:
				this.Item.Index = 0;
				break;
			case Gdk.Key.End:
			case Gdk.Key.KP_End:
				this.Item.Index = this.Query.Count - 1;
				break;
			case Gdk.Key.Page_Down:
			case Gdk.Key.KP_Page_Down:
			case Gdk.Key.space:
			case Gdk.Key.KP_Space:
				this.Item.MoveNext ();
				break;
			case Gdk.Key.Key_0:
			case Gdk.Key.KP_0:
				this.Fit = true;
				break;
			case Gdk.Key.Key_1:
			case Gdk.Key.KP_1:
				this.Zoom =  1.0;
				break;
			case Gdk.Key.Key_2:
			case Gdk.Key.KP_2:
				this.Zoom = 2.0;
				break;
			case Gdk.Key.minus:
			case Gdk.Key.KP_Subtract:
				ZoomOut ();
				break;
			case Gdk.Key.s:
				if (sharpener == null) {
					sharpener = new Sharpener (this);
					sharpener.Destroyed += HandleLoupeDestroy;
				}

				sharpener.Show ();
				break;
			case Gdk.Key.v:
				if (loupe == null) {
					loupe = new Loupe (this);
					loupe.Destroyed += HandleLoupeDestroy;
				}

				loupe.Show ();
				break;
			case Gdk.Key.equal:
			case Gdk.Key.plus:
			case Gdk.Key.KP_Add:
				ZoomIn ();
				break;
			default:
				args.RetVal = false;
				return;
			}

			return;
		}
		
		[GLib.ConnectBefore]
		private void HandleScrollEvent (object sender, Gtk.ScrollEventArgs args)
		{
			//For right now we just disable fit mode and let the parent event handlers deal
			//with the real actions.
			this.Fit = false;
		}
		
		private void HandleDestroyed (object sender, System.EventArgs args)
		{
			//loader.AreaUpdated -= HandlePixbufAreaUpdated;
			//loader.AreaPrepared -= HandlePixbufPrepared;
			loader.Dispose ();
		}
	}
}
